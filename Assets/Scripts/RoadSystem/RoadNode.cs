using UnityEngine;
using System.Collections.Generic;

public class RoadNode : MonoBehaviour
{
    [Header("Graph Connections")]
    public List<RoadSegment> ConnectedRoads = new List<RoadSegment>();

    public long OSM_NodeID; // for debugging, OSM lib id

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(transform.position, 1f);
    }
}