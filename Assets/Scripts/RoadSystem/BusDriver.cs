using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Collections.Generic;
using System.Linq;

public class BusDriver : NetworkBehaviour
{
    [Header("Configuration")]
    [Tooltip("Base speed in Units/Sec")]
    public float baseSpeed = 20f; 
    
    [Tooltip("Multiplier for Clients to ensure they arrive before Server")]
    public float clientSpeedBuffer = 1.1f; 
    public float rotationSpeed = 10f;
    
    [Header("Network State")]
    private readonly NetworkVariable<BusNetworkState> _netState = new NetworkVariable<BusNetworkState>(
        new BusNetworkState { IsInService = false },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsBroken => _netState.Value.IsBrokenDown;

    // Server Side Data
    private BusData _serverEntry; // Updated from DepotBusEntry
    private DepotController _serverDepot;
    private Route _serverRoute;
    private int _serverRouteIndex;
    
    // Server Ghost Simulation
    private float _serverDistanceTraveled; 
    private float _serverCurrentLegLength;
    private bool _serverIsWaiting;
    private float _serverWaitTimer;

    // Client Side Simulation
    private struct PathLeg
    {
        public RoadSegment Segment;
        public float Length;
        public bool HeadingToB; // True = A->B (0->1), False = B->A (1->0)
        public float StartT;    // For start/end segments (0 or 1 for full segments)
        public float EndT;
    }

    private List<PathLeg> _localPathSegments;
    private float _clientDistanceTraveled; 
    private float _totalLegLength;
    private bool _clientIsMoving;

    public override void OnNetworkSpawn()
    {
        _netState.OnValueChanged += OnNetworkStateChanged;

        if (_netState.Value.IsInService)
        {
            OnNetworkStateChanged(default, _netState.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        _netState.OnValueChanged -= OnNetworkStateChanged;
    }

    // Updated Signature to match new BusData class
    public void ServerInitialize(BusData entry, DepotController depot)
    {
        if (!IsServer) return;

        _serverEntry = entry;
        _serverDepot = depot;
        _serverRoute = TransportManager.Instance.GetRoute(entry.Schedule.RouteID);

        if (_serverRoute == null) { DespawnBus(); return; }

        _serverRouteIndex = 0; 
        
        _serverIsWaiting = true;
        _serverWaitTimer = 0.5f; 
        
        string firstStop = _serverRoute.StopIDs[0];
        
        BusNetworkState initState = new BusNetworkState
        {
            CurrentRouteID = _serverRoute.RouteID,
            PreviousStopID = firstStop,
            TargetStopID = firstStop, 
            DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay,
            IsReverseDirection = false,
            IsInService = true
        };
        _netState.Value = initState;
    }

    private void Update()
    {
        if (IsServer) ServerUpdateLoop();
        if (IsClient) ClientUpdateLoop();
    }

    private void ServerUpdateLoop()
    {
        if (!_netState.Value.IsInService || _netState.Value.IsBrokenDown) return;

        float dt = Time.deltaTime * SimulationTimeManager.Instance.TimeMultiplier;

        if (_serverIsWaiting)
        {
            _serverWaitTimer -= (dt * SimulationTimeManager.Instance.baseMinutesPerSecond) / 60f; 
            
            if (_serverWaitTimer <= 0)
            {
                ServerStartNextLeg();
            }
        }
        else
        {
            float trafficModifier = 1.0f; 

            float step = baseSpeed * trafficModifier * dt;
            _serverDistanceTraveled += step;

            if (_serverDistanceTraveled >= _serverCurrentLegLength)
            {
                ServerArriveAtStop();
            }
        }
    }

    // Called by MaintenanceManager on Server
    public void SetBrokenDown(bool isBroken)
    {
        if (!IsServer) return;
        var state = _netState.Value;
        state.IsBrokenDown = isBroken;
        _netState.Value = state;
    }

    private void ServerStartNextLeg()
    {
        var state = _netState.Value;
        int nextIndex = _serverRouteIndex + (state.IsReverseDirection ? -1 : 1);

        if (nextIndex >= _serverRoute.StopIDs.Count || nextIndex < 0)
        {
            if (_serverRoute.StopIDs.First() == _serverRoute.StopIDs.Last())
            {
                nextIndex = (nextIndex >= _serverRoute.StopIDs.Count) ? 1 : _serverRoute.StopIDs.Count - 2;
            }
            else
            {
                state.IsReverseDirection = !state.IsReverseDirection;
                nextIndex = _serverRouteIndex + (state.IsReverseDirection ? -1 : 1);
            }
        }

        // Use BusData schedule
        if (_serverEntry.Schedule.EndTime < SimulationTimeManager.Instance.CurrentTimeOfDay)
        {
            DespawnBus();
            return;
        }

        string fromID = _serverRoute.StopIDs[_serverRouteIndex];
        string toID = _serverRoute.StopIDs[nextIndex];
        _serverRouteIndex = nextIndex;

        _serverCurrentLegLength = CalculatePathDistanceServer(fromID, toID);
        _serverDistanceTraveled = 0f;
        _serverIsWaiting = false;

        state.PreviousStopID = fromID;
        state.TargetStopID = toID;
        state.DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        
        _netState.Value = state;
    }

    private void ServerArriveAtStop()
    {
        _serverIsWaiting = true;
        float minutesToWait = _serverEntry.Schedule.TurnaroundWait; 
        _serverWaitTimer = minutesToWait / 60f; 
    }

    private float CalculatePathDistanceServer(string fromID, string toID)
    {
        BusStop a = TransportManager.Instance.GetStop(fromID);
        BusStop b = TransportManager.Instance.GetStop(toID);
        if(!a || !b) return 10f; 

        var nodes = TransportManager.Instance.GetPath(a, b);
        if (nodes == null) return Vector3.Distance(a.transform.position, b.transform.position);

        float totalDist = 0f;
        
        RoadSegment startSeg = a.parentSegment;
        if(startSeg)
        {
            float exitT = (nodes[0] == startSeg.NodeA) ? 0f : 1f;
            totalDist += Mathf.Abs(exitT - a.splineT) * startSeg.Length;
        }

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            foreach (var seg in nodes[i].ConnectedRoads)
            {
                if (seg.GetConnectedNode(nodes[i]) == nodes[i + 1])
                {
                    totalDist += seg.Length;
                    break;
                }
            }
        }

        RoadSegment endSeg = b.parentSegment;
        if(endSeg && endSeg != startSeg) 
        {
            float entryT = (nodes.Last() == endSeg.NodeA) ? 0f : 1f;
            totalDist += Mathf.Abs(b.splineT - entryT) * endSeg.Length;
        }
        else if(endSeg == startSeg)
        {
             totalDist = Mathf.Abs(b.splineT - a.splineT) * startSeg.Length;
        }

        return totalDist;
    }

    private void DespawnBus()
    {
        // Fix: Use BusID string for the new DepotController method
        if(_serverDepot != null) _serverDepot.ReturnBusToDepot(_serverEntry.BusID);
    }

    // Client Logic (Visuals) - No changes needed, relies on NetworkVariable
    private void OnNetworkStateChanged(BusNetworkState oldState, BusNetworkState newState)
    {
        if (!newState.IsInService) return;

        BusStop from = TransportManager.Instance.GetStop(newState.PreviousStopID.ToString());
        BusStop to = TransportManager.Instance.GetStop(newState.TargetStopID.ToString());

        if (from != null && to != null)
        {
            if (from == to)
            {
                _clientIsMoving = false;
                transform.position = from.transform.position;
                transform.rotation = from.transform.rotation;
                return;
            }

            ReconstructLocalPath(from, to);
            
            float currentGameTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
            float timePassedGameHours = currentGameTime - newState.DepartureTime;
            if (timePassedGameHours < 0) timePassedGameHours += 24f;

            float timeMult = SimulationTimeManager.Instance.TimeMultiplier > 0 ? SimulationTimeManager.Instance.TimeMultiplier : 1f;
            float realSecondsPassed = (timePassedGameHours * 60f) / (SimulationTimeManager.Instance.baseMinutesPerSecond * timeMult);

            _clientDistanceTraveled = realSecondsPassed * baseSpeed * clientSpeedBuffer;
            _clientIsMoving = true;
        }
    }

    private void ReconstructLocalPath(BusStop from, BusStop to)
    {
        _localPathSegments = new List<PathLeg>();
        _totalLegLength = 0f;
        _clientDistanceTraveled = 0f;

        var nodes = TransportManager.Instance.GetPath(from, to);
        
        if (nodes == null || nodes.Count == 0)
        {
            if (from.parentSegment == to.parentSegment)
            {
                AddPathLeg(from.parentSegment, from.splineT, to.splineT);
            }
            return;
        }

        RoadSegment startSeg = from.parentSegment;
        if(startSeg)
        {
            float exitT = (nodes[0] == startSeg.NodeA) ? 0f : 1f;
            AddPathLeg(startSeg, from.splineT, exitT);
        }

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            RoadNode nA = nodes[i];
            RoadNode nB = nodes[i + 1];
            
            foreach (var seg in nA.ConnectedRoads)
            {
                if (seg.GetConnectedNode(nA) == nB)
                {
                    float tStart = (seg.NodeA == nA) ? 0f : 1f;
                    float tEnd = (seg.NodeA == nA) ? 1f : 0f;
                    AddPathLeg(seg, tStart, tEnd);
                    break;
                }
            }
        }

        RoadSegment endSeg = to.parentSegment;
        if(endSeg && endSeg != startSeg) 
        {
            float entryT = (nodes.Last() == endSeg.NodeA) ? 0f : 1f;
            AddPathLeg(endSeg, entryT, to.splineT);
        }
        else if (endSeg && endSeg == startSeg)
        {
            _localPathSegments.Clear();
            _totalLegLength = 0f;
            AddPathLeg(startSeg, from.splineT, to.splineT);
        }
    }

    private void AddPathLeg(RoadSegment seg, float tStart, float tEnd)
    {
        PathLeg leg = new PathLeg();
        leg.Segment = seg;
        leg.Length = Mathf.Abs(tEnd - tStart) * seg.Length;
        leg.StartT = tStart;
        leg.EndT = tEnd;
        leg.HeadingToB = tEnd > tStart; 
        
        _localPathSegments.Add(leg);
        _totalLegLength += leg.Length;
    }

    private void ClientUpdateLoop()
    {
        if (!_clientIsMoving || _localPathSegments == null || _localPathSegments.Count == 0 || _netState.Value.IsBrokenDown) return;

        float dt = Time.deltaTime * SimulationTimeManager.Instance.TimeMultiplier;
        float localTraffic = 1.0f;
        float step = baseSpeed * localTraffic * clientSpeedBuffer * dt;
        
        _clientDistanceTraveled += step;

        if (_clientDistanceTraveled >= _totalLegLength)
        {
            _clientDistanceTraveled = _totalLegLength;
            _clientIsMoving = false;
        }

        UpdateTransformOnSpline(_clientDistanceTraveled);
    }

    private void UpdateTransformOnSpline(float currentDist)
    {
        Vector3 pos = CalculatePoint(currentDist, out Vector3 currentTangent);
        transform.position = pos;

        float lookDist = currentDist + 1.0f;
        if (lookDist > _totalLegLength) lookDist = _totalLegLength;

        if (lookDist - currentDist > 0.01f)
        {
            Vector3 lookPos = CalculatePoint(lookDist, out _);
            Vector3 dir = lookPos - pos;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed * SimulationTimeManager.Instance.TimeMultiplier);
            }
        }
    }

    private Vector3 CalculatePoint(float dist, out Vector3 tangent)
    {
        tangent = Vector3.forward;
        float remaining = dist;

        foreach (var leg in _localPathSegments)
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

        if (_localPathSegments.Count > 0)
        {
            var last = _localPathSegments.Last();
            return last.Segment.GetPointOnRoad(last.EndT, last.HeadingToB);
        }

        return transform.position;
    }

    
}