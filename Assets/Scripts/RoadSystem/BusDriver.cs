using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class BusDriver : MonoBehaviour
{
    [Header("Configuration")]
    public float speed = 10f; // Real-time speed (approx units/sec)
    public float rotationSpeed = 5f;
    [Tooltip("Time to wait at intermediate stops (Game Minutes)")]
    public float intermediateStopWaitTime = 10f; 

    [Header("Runtime Info")]
    [SerializeField] private string _currentRouteID;
    [SerializeField] private string _currentDestStopName;
    [SerializeField] private bool _isReversing; 

    private DepotBusEntry _myEntry;
    private DepotController _myDepot;
    private Route _activeRoute;

    public enum DriverState { Idle, Driving, WaitingAtStop, WaitingAtTerminus, Completed }
    [SerializeField] private DriverState _driverState = DriverState.Idle;
    public DriverState CurrentState => _driverState; // For future UI

    public void Initialize(DepotBusEntry entry, DepotController depot)
    {
        _myEntry = entry;
        _myDepot = depot;
        _currentRouteID = entry.Schedule.RouteID;
        _activeRoute = TransportManager.Instance.ActiveRoutes.Find(r => r.RouteID == _currentRouteID);
        
        if (_activeRoute != null && _activeRoute.StopIDs.Count >= 2)
        {
            StartCoroutine(RunSchedule());
        }
        else
        {
            Debug.LogError($"BusDriver: Invalid route {_currentRouteID}");
            _myDepot.ReturnBusToDepot(_myEntry);
        }
    }

    private IEnumerator RunSchedule()
    {
        // Default Start: Forward
        _isReversing = false;

        while (true)
        {
            // 1. Prepare Stop List for this Leg
            List<string> stopsToVisit = new List<string>(_activeRoute.StopIDs);
            if (_isReversing) stopsToVisit.Reverse();

            // 2. Drive the Leg (Stop by Stop)
            for (int i = 0; i < stopsToVisit.Count - 1; i++)
            {
                string currentStopID = stopsToVisit[i];
                string nextStopID = stopsToVisit[i + 1];

                BusStop fromStop = TransportManager.Instance.GetStop(currentStopID);
                BusStop toStop = TransportManager.Instance.GetStop(nextStopID);
                
                _currentDestStopName = toStop.name;
                _driverState = DriverState.Driving;

                // A. Drive
                yield return StartCoroutine(MoveAlongPath(fromStop, toStop));

                // B. Handle Arrival
                bool isTerminus = (i + 1 == stopsToVisit.Count - 1);
                
                if (isTerminus)
                {
                    _driverState = DriverState.WaitingAtTerminus;
                    // Wait Turnaround Time
                    yield return StartCoroutine(WaitRoutine(_myEntry.Schedule.TurnaroundWait));
                }
                else
                {
                    _driverState = DriverState.WaitingAtStop;
                    // Wait Standard Stop Time
                    yield return StartCoroutine(WaitRoutine(intermediateStopWaitTime));
                }
            }

            // 3. End of Leg Decision
            // Check if Shift is Over
            if (SimulationTimeManager.Instance.CurrentTimeOfDay >= _myEntry.Schedule.EndTime)
            {
                _driverState = DriverState.Completed;
                _myDepot.ReturnBusToDepot(_myEntry);
                yield break; // Stop Coroutine
            }

            // Prepare for Next Leg
            bool isRing = _activeRoute.StopIDs.First() == _activeRoute.StopIDs.Last();
            
            if (isRing)
            {
                // Ring Route: Just loop forward again
                _isReversing = false;
            }
            else
            {
                // Linear Route: Toggle Direction
                // We are physically at End. Reversed list starts at End. Matches perfectly.
                _isReversing = !_isReversing;
            }
            
            // Loop continues -> Generates new stop list -> Drives
        }
    }

    private IEnumerator WaitRoutine(float gameMinutes)
    {
        float hoursToWait = gameMinutes / 60f;
        float targetTime = SimulationTimeManager.Instance.CurrentTimeOfDay + hoursToWait;
        
        float startHour = SimulationTimeManager.Instance.CurrentTimeOfDay;
        
        // Loop until enough game-time has passed
        while (true)
        {
            float currentHour = SimulationTimeManager.Instance.CurrentTimeOfDay;
            
            // Handle day wrap for calculation (if current < start, we wrapped)
            float adjustedCurrent = (currentHour < startHour) ? currentHour + 24f : currentHour;
            
            if (adjustedCurrent >= startHour + hoursToWait)
            {
                break;
            }
            yield return null;
        }
    }

    // Movement

    private IEnumerator MoveAlongPath(BusStop from, BusStop to)
    {
        // Use the new TransportManager method that calculates if missing
        List<RoadNode> path = TransportManager.Instance.GetPath(from, to);

        if (path == null)
        {
            // Fallback: Same segment?
            if (from.parentSegment == to.parentSegment)
            {
                yield return StartCoroutine(DriveSegment(from.parentSegment, from.splineT, to.splineT));
            }
            yield break;
        }

        // 1. Exit Start Segment
        RoadSegment startSeg = from.parentSegment;
        float exitT = (path[0] == startSeg.NodeA) ? 0f : 1f;
        yield return StartCoroutine(DriveSegment(startSeg, from.splineT, exitT));

        // 2. Intermediates
        for (int i = 0; i < path.Count - 1; i++)
        {
            RoadNode a = path[i];
            RoadNode b = path[i+1];
            RoadSegment road = FindConnection(a, b);
            if (road != null)
            {
                float t1 = (road.NodeA == a) ? 0f : 1f;
                float t2 = (road.NodeA == a) ? 1f : 0f;
                yield return StartCoroutine(DriveSegment(road, t1, t2));
            }
        }

        // 3. Enter Target Segment
        RoadSegment endSeg = to.parentSegment;
        float entryT = (path.Last() == endSeg.NodeA) ? 0f : 1f;
        yield return StartCoroutine(DriveSegment(endSeg, entryT, to.splineT));
    }

    private IEnumerator DriveSegment(RoadSegment segment, float tStart, float tEnd)
    {
        if (Mathf.Abs(tEnd - tStart) < 0.001f) yield break;

        float dist = Mathf.Abs(tEnd - tStart) * segment.Length;
        // Visual speed is constant (real-time), but simulation waits are game-time.
        float duration = dist / speed; 
        
        float elapsed = 0f;
        bool headingToB = tEnd > tStart;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;
            float currentT = Mathf.Lerp(tStart, tEnd, progress);

            Vector3 pos = segment.GetPointOnRoad(currentT, headingToB);
            
            // Lookahead
            float lookT = Mathf.Lerp(tStart, tEnd, Mathf.Min(1f, progress + 0.05f));
            Vector3 lookPos = segment.GetPointOnRoad(lookT, headingToB);
            
            transform.position = pos;
            if (Vector3.Distance(pos, lookPos) > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, 
                    Quaternion.LookRotation(lookPos - pos), 
                    Time.deltaTime * rotationSpeed);
            }

            yield return null;
        }
    }

    private RoadSegment FindConnection(RoadNode a, RoadNode b)
    {
        foreach (var seg in a.ConnectedRoads)
        {
            if (seg.GetConnectedNode(a) == b) return seg;
        }
        return null;
    }
}