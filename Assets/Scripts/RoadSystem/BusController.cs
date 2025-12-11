using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class BusController : MonoBehaviour
{
    public float speed = 10f; 
    public float waitTimeAtStop = 2.0f;

    public string assignedRouteID;
    public Route currentRoute;
    public int nextStopIndex = 0;
    
    void Start()
    {
        if (string.IsNullOrEmpty(assignedRouteID) && TransportManager.Instance != null && TransportManager.Instance.ActiveRoutes.Count > 0)
            AssignRoute(TransportManager.Instance.ActiveRoutes[0]);
    }

    public void AssignRoute(Route route)
    {
        currentRoute = route;
        assignedRouteID = route.RouteID;
        nextStopIndex = 0;

        if (currentRoute.StopIDs.Count > 0)
        {
            BusStop firstStop = TransportManager.Instance.GetStop(currentRoute.StopIDs[0]);
            if (firstStop != null)
            {
                // Spawn: Default to Forward (Facing NodeB)
                transform.position = firstStop.parentSegment.GetPointOnRoad(firstStop.splineT, true);
                var container = firstStop.parentSegment.GetComponent<UnityEngine.Splines.SplineContainer>();
                Vector3 tangent = container.EvaluateTangent(firstStop.splineT);
                if(tangent != Vector3.zero) transform.rotation = Quaternion.LookRotation(tangent);
                
                StartCoroutine(BusRoutine());
            }
        }
    }

    private IEnumerator BusRoutine()
    {
        while (nextStopIndex < currentRoute.StopIDs.Count - 1)
        {
            BusStop startStop = TransportManager.Instance.GetStop(currentRoute.StopIDs[nextStopIndex]);
            BusStop targetStop = TransportManager.Instance.GetStop(currentRoute.StopIDs[nextStopIndex + 1]);

            yield return new WaitForSeconds(waitTimeAtStop);

            RoadSegment startSeg = startStop.parentSegment;
            RoadSegment endSeg = targetStop.parentSegment;

            // 1. Get Cached Path from Manager
            List<RoadNode> path = TransportManager.Instance.GetCachedPath(startStop, targetStop);

            if (path == null)
            {
                // Edge Case: Same Segment?
                if (startSeg == endSeg)
                {
                    yield return StartCoroutine(DriveSegment(startSeg, startStop.splineT, targetStop.splineT));
                }
                else
                {
                    Debug.LogError($"Bus {name}: No path found for {startStop.name}->{targetStop.name}");
                    yield break;
                }
            }
            else
            {
                // 2. Drive to Exit Node of Current Road
                // Path[0] is the node we leave FROM.
                RoadNode exitNode = path[0];
                float exitT = (exitNode == startSeg.NodeA) ? 0.0f : 1.0f;
                yield return StartCoroutine(DriveSegment(startSeg, startStop.splineT, exitT));

                // 3. Drive Inter-segment Path
                for (int i = 0; i < path.Count - 1; i++)
                {
                    RoadNode curr = path[i];
                    RoadNode next = path[i+1];
                    
                    RoadSegment connection = FindConnection(curr, next);
                    if (connection != null)
                    {
                        // Direction: If NodeA is curr, we are at 0, going to 1 (B).
                        float tStart = (connection.NodeA == curr) ? 0f : 1f;
                        float tEnd   = (connection.NodeA == curr) ? 1f : 0f;
                        yield return StartCoroutine(DriveSegment(connection, tStart, tEnd));
                    }
                }

                // 4. Drive from Entry Node to Destination Stop
                RoadNode entryNode = path[path.Count - 1];
                float entryT = (entryNode == endSeg.NodeA) ? 0.0f : 1.0f;
                yield return StartCoroutine(DriveSegment(endSeg, entryT, targetStop.splineT));
            }

            nextStopIndex++;
        }

        Debug.Log("Bus completed route. Despawning.");
        Destroy(gameObject);
    }

    private IEnumerator DriveSegment(RoadSegment segment, float tStart, float tEnd)
    {
        if(Mathf.Abs(tEnd - tStart) < 0.001f) yield break;

        float totalDist = Mathf.Abs(tEnd - tStart) * segment.Length;
        float duration = totalDist / speed;
        float elapsed = 0f;
        
        // If tEnd > tStart, we are increasing T (0->1) -> Heading to B (Forward)
        // If tEnd < tStart, we are decreasing T (1->0) -> Heading to A (Backward)
        bool headingToB = tEnd > tStart;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;
            float currentT = Mathf.Lerp(tStart, tEnd, progress);

            // This calculates offset based on direction
            Vector3 pos = segment.GetPointOnRoad(currentT, headingToB);
            
            // Simple Lookahead
            float lookT = Mathf.Lerp(tStart, tEnd, Mathf.Min(1f, progress + 0.1f));
            Vector3 lookPos = segment.GetPointOnRoad(lookT, headingToB);

            transform.position = pos;
            if (Vector3.Distance(pos, lookPos) > 0.01f)
                transform.LookAt(lookPos);

            yield return null;
        }
    }

    private RoadSegment FindConnection(RoadNode a, RoadNode b)
    {
        foreach(var seg in a.ConnectedRoads)
        {
            if (seg.GetConnectedNode(a) == b) return seg;
        }
        return null;
    }
}