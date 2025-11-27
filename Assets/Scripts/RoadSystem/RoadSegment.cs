using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(SplineContainer))]
public class RoadSegment : MonoBehaviour
{
    [Header("Graph Topology")]
    public RoadNode StartNode;
    public RoadNode EndNode;

    [Header("Metadata")]
    public float SpeedLimit = 50f; // km/h
    public float Length;           // Cached length in meters

    private SplineContainer container;

    public Spline Spline
    {
        get
        {
            if (container == null) container = GetComponent<SplineContainer>();
            return container.Spline;
        }
    }

    public float GetCost()
    {
        // A* Cost = Distance / Speed
        // Avoid divide by zero
        return Length / (Mathf.Max(1, SpeedLimit));
    }

    // Helper to calculate length automatically
    public void CalculateLength()
    {
        if (container == null) container = GetComponent<SplineContainer>();
        Length = container.CalculateLength();
    }
}