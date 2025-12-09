using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class RouteVisualizer : MonoBehaviour
{
    private LineRenderer lr;
    
    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = 2f; 
        lr.positionCount = 0;
    }

    public void DrawRoute(Route route)
    {
        if (route == null || route.StopIDs.Count < 2)
        {
            lr.positionCount = 0;
            return;
        }

        lr.material.color = route.RouteColor;
        List<Vector3> pathPoints = new List<Vector3>();

        // Iterate through stops and find path between them
        for (int i = 0; i < route.StopIDs.Count - 1; i++)
        {
            BusStop start = TransportManager.Instance.GetStop(route.StopIDs[i]);
            BusStop end = TransportManager.Instance.GetStop(route.StopIDs[i+1]);

            if (start == null || end == null) continue;

            // 1. Add Start Position
            pathPoints.Add(start.transform.position + Vector3.up);

            // 2. Find path logic
            // Get nodes from the stops' parent segments
            RoadNode nodeA = start.parentSegment.GetConnectedNode(null); // Just get one end
            // Actually, we need to pathfind from Segment to Segment. 
            // For simplicity, let's pathfind from NodeA of StartSegment to NodeA of EndSegment
            // Ideally, you'd project to nearest node.
            
            RoadNode pathStartNode = start.parentSegment.NodeB; // Assuming forward flow
            RoadNode pathEndNode = end.parentSegment.NodeA;

            var nodePath = RoadPathfinder.FindPath(pathStartNode, pathEndNode);
            
            if (nodePath != null)
            {
                foreach (var node in nodePath)
                {
                    pathPoints.Add(node.transform.position + Vector3.up);
                }
            }

            // 3. Add End Position
            pathPoints.Add(end.transform.position + Vector3.up);
        }

        lr.positionCount = pathPoints.Count;
        lr.SetPositions(pathPoints.ToArray());
    }

    public void Clear()
    {
        lr.positionCount = 0;
    }
}