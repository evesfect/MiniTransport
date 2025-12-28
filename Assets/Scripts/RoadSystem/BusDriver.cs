using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class BusDriver : VehicleDriver
{
    [Header("Debug")]
    public MarkerSpawner debugMarkerSpawner;

    // Properties baseSpeed, clientSpeedBuffer, rotationSpeed are in Base Class

    [Header("Network State")]
    private readonly NetworkVariable<BusNetworkState> _netState = new NetworkVariable<BusNetworkState>(
        new BusNetworkState { IsInService = false },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Breakdown logic
    public bool IsBroken => _netState.Value.IsBrokenDown;
    public string PreviousStopID => _netState.Value.PreviousStopID.ToString();
    public string TargetStopID => _netState.Value.TargetStopID.ToString();

    public float RemainingPathDistance => Mathf.Max(0f, m_ServerCurrentLegLength - m_ServerDistanceTraveled);

    // Server Side Data ()
    private BusData _serverEntry;
    private DepotController _serverDepot;
    private Route _serverRoute;
    private int _serverRouteIndex;
    
    // Server Ghost Simulation (: Waiting at stops)
    private bool _serverIsWaiting;
    private float _serverWaitTimer;

    // Client Side Simulation
    // PathLeg struct and Lists moved to Base Class (m_ServerPathSegments, m_LocalPathSegments, etc)

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

    // Initialization requires BusData and Depot
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
        // Check Service/Broken status
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
            
            // Uses base class path list (m_ServerPathSegments)
            if (GridManager.Instance != null && m_ServerPathSegments.Count > 0)
            {
                // Uses base class math (CalculatePoint)
                Vector3 serverPos = CalculatePoint(m_ServerDistanceTraveled, m_ServerPathSegments, out _);
                trafficModifier = GridManager.Instance.GetTrafficModifierAt(serverPos);
            }

            float step = baseSpeed * trafficModifier * dt;
            m_ServerDistanceTraveled += step;

            // Checks base class leg length
            if (m_ServerDistanceTraveled >= m_ServerCurrentLegLength)
            {
                ServerArriveAtStop();
            }
        }
    }

    // Maintenance Logic
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

        // Schedule Check
        if (_serverEntry.Schedule.EndTime < SimulationTimeManager.Instance.CurrentTimeOfDay)
        {
            DespawnBus();
            return;
        }

        string fromID = _serverRoute.StopIDs[_serverRouteIndex];
        string toID = _serverRoute.StopIDs[nextIndex];
        _serverRouteIndex = nextIndex;

        BusStop fromStop = TransportManager.Instance.GetStop(fromID);
        BusStop toStop = TransportManager.Instance.GetStop(toID);

        if (fromStop && toStop)
        {
            // Uses Bus-Specific logic to find nodes, then fills Generic m_ServerPathSegments
            BuildPathSegments(fromStop, toStop, m_ServerPathSegments, out m_ServerCurrentLegLength);
        }
        else
        {
            m_ServerPathSegments.Clear();
            m_ServerCurrentLegLength = 10f; // Fallback
        }

        m_ServerDistanceTraveled = 0f;
        _serverIsWaiting = false;

        state.PreviousStopID = fromID;
        state.TargetStopID = toID;
        state.DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        
        _netState.Value = state;
    }

    private void ServerArriveAtStop()
    {
        _serverIsWaiting = true;
        // Wait time from Schedule
        float minutesToWait = _serverEntry.Schedule.TurnaroundWait; 
        _serverWaitTimer = minutesToWait / 60f; 
    }

    private void DespawnBus()
    {
        if(_serverDepot != null) _serverDepot.ReturnBusToDepot(_serverEntry.BusID);
    }

    // Client Logic (Visuals)
    private void OnNetworkStateChanged(BusNetworkState oldState, BusNetworkState newState)
    {
        if (!newState.IsInService) return;

        BusStop from = TransportManager.Instance.GetStop(newState.PreviousStopID.ToString());
        BusStop to = TransportManager.Instance.GetStop(newState.TargetStopID.ToString());

        if (from != null && to != null)
        {
            if (from == to)
            {
                m_ClientIsMoving = false;
                transform.position = from.transform.position;
                transform.rotation = from.transform.rotation;
                return;
            }

            BuildPathSegments(from, to, m_LocalPathSegments, out m_TotalLegLength);
            
            float currentGameTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
            float timePassedGameHours = currentGameTime - newState.DepartureTime;
            if (timePassedGameHours < 0) timePassedGameHours += 24f;

            float timeMult = SimulationTimeManager.Instance.TimeMultiplier > 0 ? SimulationTimeManager.Instance.TimeMultiplier : 1f;
            float realSecondsPassed = (timePassedGameHours * 60f) / (SimulationTimeManager.Instance.baseMinutesPerSecond * timeMult);

            m_ClientDistanceTraveled = realSecondsPassed * baseSpeed * clientSpeedBuffer;
            m_ClientIsMoving = true;
        }
    }

    private void ClientUpdateLoop()
    {
        // Check Broken status
        if (!m_ClientIsMoving || m_LocalPathSegments == null || m_LocalPathSegments.Count == 0 || _netState.Value.IsBrokenDown) return;

        float dt = Time.deltaTime * SimulationTimeManager.Instance.TimeMultiplier;

        // TRAFFIC CHECK (Generic concept, but logic kept here for flow control)
        float localTraffic = 1.0f;
        if (GridManager.Instance != null)
        {
            localTraffic = GridManager.Instance.GetTrafficModifierAt(transform.position);
        }

        float step = baseSpeed * localTraffic * clientSpeedBuffer * dt;
        
        m_ClientDistanceTraveled += step;

        if (m_ClientDistanceTraveled >= m_TotalLegLength)
        {
            m_ClientDistanceTraveled = m_TotalLegLength;
            m_ClientIsMoving = false;
        }

        // Update visuals
        UpdateTransformOnSpline(m_ClientDistanceTraveled, m_LocalPathSegments);
    }

    // Path building strategy (Stop to Stop)
    private void BuildPathSegments(BusStop from, BusStop to, List<PathLeg> targetList, out float totalLength)
    {
        targetList.Clear();
        totalLength = 0f;

        var nodes = TransportManager.Instance.GetPath(from, to);
        
        // 1. Handle Direct/Short Paths
        if (nodes == null || nodes.Count == 0)
        {
            if (from.parentSegment == to.parentSegment && from.parentSegment != null)
            {
                // Uses Base Class helper AddPathLeg
                AddPathLeg(from.parentSegment, from.splineT, to.splineT, targetList, ref totalLength);
            }
            else
            {
                // Fallback for disjointed stops
                totalLength = Vector3.Distance(from.transform.position, to.transform.position);
            }
            return;
        }

        // 2. Start Segment
        RoadSegment startSeg = from.parentSegment;
        if(startSeg)
        {
            float exitT = (nodes[0] == startSeg.NodeA) ? 0f : 1f;
            AddPathLeg(startSeg, from.splineT, exitT, targetList, ref totalLength);
        }

        // 3. Middle Segments
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
                    AddPathLeg(seg, tStart, tEnd, targetList, ref totalLength);
                    break;
                }
            }
        }

        // 4. End Segment
        RoadSegment endSeg = to.parentSegment;
        if(endSeg && endSeg != startSeg) 
        {
            float entryT = (nodes.Last() == endSeg.NodeA) ? 0f : 1f;
            AddPathLeg(endSeg, entryT, to.splineT, targetList, ref totalLength);
        }
    }

    [ContextMenu("Debug Server Position")]
public void DebugShowServerPosition()
{
    // This only works on the Server instance
    if (!IsServer && !IsHost) 
    {
        Debug.LogWarning("Cannot debug Server Position from a Client instance.");
        return;
    }

    if (GetCurrentSegmentAndT(out RoadSegment seg, out float t, out bool headingToB))
    {
        if (debugMarkerSpawner != null)
        {
            // Calculate the exact world position based on SERVER data
            Vector3 serverPos = seg.GetPointOnRoad(t, headingToB);
            debugMarkerSpawner.SpawnMarkerAtHitLocation(serverPos);
            Debug.Log($"[Bus Server Debug] Dist: {m_ServerDistanceTraveled:F1}, Seg: {seg.name}, T: {t:F2}");
        }
    }
}
}