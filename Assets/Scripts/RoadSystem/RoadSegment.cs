using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(SplineContainer))]
public class RoadSegment : MonoBehaviour
{
    [Header("Graph Topology")]
    public RoadNode NodeA;
    public RoadNode NodeB;

    [Header("Lane Settings")]
    [Tooltip("Distance from the center spline to the lane center.")]
    public float laneOffset = 2.0f; 

    [Header("Metadata")]
    public float SpeedLimit = 50f;
    public float Length;

    private SplineContainer _container;
    public SplineContainer Container 
    {
        get 
        {
            if (_container == null) _container = GetComponent<SplineContainer>();
            return _container;
        }
    }

    public Spline Spline => Container.Spline;

    private void Awake()
    {
        CalculateLength();
    }

    public Vector3 GetPointOnRoad(float t, bool headingToNodeB) // world space position
    {
        // Evaluate World Position and Tangent directly from Container
        Vector3 pos = (Vector3)Container.EvaluatePosition(t);
        Vector3 tangent = (Vector3)Container.EvaluateTangent(t);
        Vector3 upVector = (Vector3)Container.EvaluateUpVector(t);

        // Calculate Right Vector
        // Cross(Up, Tangent) gives the vector pointing "Right" relative to the road
        Vector3 roadRight = Vector3.Cross(upVector, tangent).normalized;

        // Determine Offset
        // If heading to B (Forward), +Offset. If to A (Backward), -Offset.
        float finalOffset = headingToNodeB ? laneOffset : -laneOffset;

        return pos + (roadRight * finalOffset);
    }

    public float GetCost()
    {
        return Length / (Mathf.Max(1, SpeedLimit));
    }

    public void CalculateLength()
    {
        Length = Container.CalculateLength();
    }
    
    // helpers
    public RoadNode GetConnectedNode(RoadNode entryNode) // get other node
    {
        if (entryNode == NodeA) return NodeB;
        if (entryNode == NodeB) return NodeA;
        return null;
    }
    
    public bool IsHeadingToNodeB(RoadNode entryNode) // determine direction
    {
        return entryNode == NodeA;
    }
}