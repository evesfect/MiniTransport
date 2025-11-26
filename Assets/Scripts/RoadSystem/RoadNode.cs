using UnityEngine;
using System.Collections.Generic;

public class RoadNode : MonoBehaviour
{
    [Header("Graph Connections")]
    // We store which segments LEAVE this node.
    // Pathfinding uses this to know where it can go next.
    public List<RoadSegment> OutgoingRoads = new List<RoadSegment>();

    // Optional: ID from OSM for debugging
    public long OSM_NodeID; 

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(transform.position, 1f);
    }
}