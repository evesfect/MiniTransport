using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

public abstract class VehicleDriver : NetworkBehaviour
{
    [Header("Vehicle Configuration")]
    [Tooltip("Base speed in Units/Sec")]
    public float baseSpeed = 50f;
    
    [Tooltip("Multiplier for Clients to ensure they arrive before Server")]
    public float clientSpeedBuffer = 1.1f; 
    public float rotationSpeed = 10f;
    
    protected struct PathLeg
    {
        public RoadSegment Segment;
        public float Length;
        public bool HeadingToB; // True = A->B (0->1), False = B->A (1->0)
        public float StartT;    // Allows partial segment travel
        public float EndT;
    }

    protected List<PathLeg> m_ServerPathSegments = new List<PathLeg>();
    protected float m_ServerDistanceTraveled; 
    protected float m_ServerCurrentLegLength;

    protected List<PathLeg> m_LocalPathSegments = new List<PathLeg>();
    protected float m_ClientDistanceTraveled; 
    protected float m_TotalLegLength;
    protected bool m_ClientIsMoving;

    /// <summary>
    /// Updates the visual transform (Position & Rotation) along the provided path based on distance.
    /// </summary>
    protected void UpdateTransformOnSpline(float currentDist, List<PathLeg> pathSegments)
    {
        Vector3 pos = CalculatePoint(currentDist, pathSegments, out Vector3 currentTangent);
        transform.position = pos;

        // Look-ahead for rotation
        float lookDist = currentDist + 1.0f;
        if (lookDist > m_TotalLegLength) lookDist = m_TotalLegLength; 

        if (lookDist - currentDist > 0.01f)
        {
            Vector3 lookPos = CalculatePoint(lookDist, pathSegments, out _);
            Vector3 dir = lookPos - pos;

            dir.y = 0;
            dir.Normalize();

            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                
                // Dependencies on SimulationTimeManager are global game state
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, 
                    targetRot, 
                    Time.deltaTime * rotationSpeed * SimulationTimeManager.Instance.TimeMultiplier
                );
            }
        }
    }

    protected Vector3 CalculatePoint(float dist, List<PathLeg> segments, out Vector3 tangent)
    {
        tangent = Vector3.forward;
        
        if (segments == null || segments.Count == 0) return transform.position;

        float remaining = dist;

        foreach (var leg in segments) 
        {
            if (remaining <= leg.Length)
            {
                float pct = remaining / leg.Length;
                float t = Mathf.Lerp(leg.StartT, leg.EndT, pct);
                
                if (leg.Segment.Container != null)
                {
                    Vector3 p = leg.Segment.GetPointOnRoad(t, leg.HeadingToB);
                    tangent = (Vector3)leg.Segment.Container.EvaluateTangent(t); 
                    return p;
                }
            }
            remaining -= leg.Length;
        }

        if (segments.Count > 0)
        {
            var last = segments.Last();
            return last.Segment.GetPointOnRoad(last.EndT, last.HeadingToB);
        }

        return transform.position;
    }

    /// <summary>
    /// Helper to construct a leg and add it to a list.
    /// </summary>
    protected void AddPathLeg(RoadSegment seg, float tStart, float tEnd, List<PathLeg> list, ref float lengthAccumulator)
    {
        PathLeg leg = new PathLeg();
        leg.Segment = seg;
        leg.Length = Mathf.Abs(tEnd - tStart) * seg.Length;
        leg.StartT = tStart;
        leg.EndT = tEnd;
        leg.HeadingToB = tEnd > tStart; 
        
        list.Add(leg);
        lengthAccumulator += leg.Length;
    }
    /// <summary>
    /// Returns the specific RoadSegment and Spline-T value for the vehicle's current position.
    /// </summary>
    public bool GetCurrentSegmentAndT(out RoadSegment segment, out float tValue, out bool HeadingToB)
    {
        segment = null;
        tValue = 0f;
        HeadingToB = true;

        if (m_ServerPathSegments == null || m_ServerPathSegments.Count == 0) return false;

        float remainingDist = m_ServerDistanceTraveled;

        foreach (var leg in m_ServerPathSegments)
        {
            // Is the vehicle inside this leg?
            if (remainingDist <= leg.Length)
            {
                segment = leg.Segment;
                
                float pct = remainingDist / leg.Length; // calculate T
                tValue = Mathf.Lerp(leg.StartT, leg.EndT, pct); // interpolate 0->1 or reverse
                HeadingToB = (leg.StartT < leg.EndT);
                return true;
            }
            
            remainingDist -= leg.Length;
        }

        // Edge case: Vehicle is exactly at the end of the path
        var lastLeg = m_ServerPathSegments.Last();
        segment = lastLeg.Segment;
        tValue = lastLeg.EndT;
        HeadingToB = (lastLeg.StartT < lastLeg.EndT);
        return true;
    }
}