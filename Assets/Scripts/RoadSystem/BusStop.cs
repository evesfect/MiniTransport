using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class BusStop : MonoBehaviour
{
    [Header("Network Data")]
    public string stopID;
    
    [Tooltip("The segment this stop is attached to.")]
    public RoadSegment parentSegment;

    [Tooltip("Position on the spline (0.0 to 1.0).")]
    [Range(0f, 1f)] 
    public float splineT;

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 1.5f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 2, new Vector3(2, 4, 2));
    }

    public void SnapToSegment()
    {
        if (parentSegment == null) return;
        
        var container = parentSegment.GetComponent<SplineContainer>();
        if (container == null) 
        {
            Debug.LogError("Parent Segment is missing a SplineContainer!");
            return;
        }
        
        // SplineContainer.EvaluatePosition returns World Space float3
        Vector3 worldPos = (Vector3)container.EvaluatePosition(splineT);
        
        transform.position = worldPos;
        
        // SplineContainer.EvaluateTangent returns World Space tangent float3
        Vector3 worldTangent = (Vector3)container.EvaluateTangent(splineT);
        
        if (worldTangent != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(worldTangent);
        }
    }
}