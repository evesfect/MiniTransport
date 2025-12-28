using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine.Splines;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;

public enum RecoveryState { Idle, MovingToTarget, Repairing, Returning, Refilling }

public struct RecoveryNetworkState : INetworkSerializable, System.IEquatable<RecoveryNetworkState>
{
    public RecoveryState CurrentState;
    public FixedString32Bytes OwnerDepotID;
    public FixedString32Bytes TargetBusID;
    
    public FixedString32Bytes TargetSegmentName;
    public float TargetSplineT;       
    public bool EnteringFromNodeA;    

    public float DepartureTime;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref CurrentState);
        serializer.SerializeValue(ref OwnerDepotID);
        serializer.SerializeValue(ref TargetBusID);
        serializer.SerializeValue(ref TargetSegmentName);
        serializer.SerializeValue(ref TargetSplineT);
        serializer.SerializeValue(ref EnteringFromNodeA);
        serializer.SerializeValue(ref DepartureTime);
    }

    public bool Equals(RecoveryNetworkState other)
    {
        return CurrentState == other.CurrentState &&
               OwnerDepotID == other.OwnerDepotID &&
               TargetBusID == other.TargetBusID &&
               TargetSegmentName == other.TargetSegmentName &&
               TargetSplineT == other.TargetSplineT &&
               EnteringFromNodeA == other.EnteringFromNodeA &&
               DepartureTime == other.DepartureTime;
    }
}

public class RecoveryVehicle : VehicleDriver
{
    [Header("Debugging")]
    public MarkerSpawner debugMarkerSpawner;

    [Header("Recovery Settings")]
    public float repairRatePerSecond = 10f;
    public float refillDuration = 2.0f; 
    public bool debugLogs = true;

    private readonly NetworkVariable<RecoveryNetworkState> _netState = new NetworkVariable<RecoveryNetworkState>(
        new RecoveryNetworkState { CurrentState = RecoveryState.Idle },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // --- Server-Side Data ---
    private DepotController _ownerDepot;
    private BusData _targetBusData;
    private RoadSegment _targetSegmentCache; 
    private bool _serverIsMoving;
    private float _currentRefillTimer;

    // Client-Side Data
    private static Dictionary<string, DepotController> _clientDepotCache; 

    // Teleport Debugging
    private Vector3 _lastFramePos;

    public bool IsBusy => _netState.Value.CurrentState != RecoveryState.Idle;

    public override void OnNetworkSpawn()
    {
        _netState.OnValueChanged += OnStateChanged;
        _lastFramePos = transform.position;

        if (_netState.Value.CurrentState != RecoveryState.Idle)
        {
            OnStateChanged(default, _netState.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        _netState.OnValueChanged -= OnStateChanged;
    }

    // --- SERVER LOGIC ---

    public void StartMission(string busID, DepotController depot)
    {
        if (!IsServer) return;
        
        _ownerDepot = depot;
        _targetBusData = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == busID);
        
        GameObject busObj = FleetManager.Instance.GetActiveBus(busID);
        if (busObj == null) { CancelMission("Target bus gameobject not found"); return; }

        BusDriver targetBus = busObj.GetComponent<BusDriver>();


        
        if (targetBus == null || !targetBus.GetCurrentSegmentAndT(out RoadSegment segment, out float splineT, out bool isHeadingToB))
        {
            CancelMission($"Bus {busID} location data unavailable.");
            return;
        }

        _targetSegmentCache = segment;

        if (_ownerDepot.SpawnNode == null)
        {
            CancelMission($"Depot {depot.depotID} has no Spawn Node.");
            return;
        }
        RoadNode startNode = _ownerDepot.SpawnNode;

        List<RoadNode> nodePath = RoadPathfinder.FindPathToSegment(startNode, segment);
        bool enteringFromA = true;
        
        if (nodePath != null && nodePath.Count > 0)
        {
            RoadNode entryNode = nodePath.Last();
            enteringFromA = (entryNode == segment.NodeA);
        }
        else
        {
            if (startNode == segment.NodeA) enteringFromA = true;
            else if (startNode == segment.NodeB) enteringFromA = false;
            else
            {
                CancelMission("No path found to target segment.");
                return;
            }
        }

        BuildServerPathToTarget(nodePath, segment, enteringFromA, splineT);

        if (debugMarkerSpawner != null)
        {
            Vector3 exactWorldPos = segment.GetPointOnRoad(splineT, enteringFromA);
            debugMarkerSpawner.SpawnMarkerAtHitLocation(exactWorldPos);
            Debug.Log("Spawned Marker for Recovery Vehicle");
        } else
        {
            Debug.Log("Marker is null!");
        }

        _netState.Value = new RecoveryNetworkState
        {
            CurrentState = RecoveryState.MovingToTarget,
            OwnerDepotID = depot.depotID,
            TargetBusID = busID,
            TargetSegmentName = segment.name,
            TargetSplineT = splineT,
            EnteringFromNodeA = enteringFromA,
            DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay
        };

        m_ServerDistanceTraveled = 0f; 
        _serverIsMoving = true;
        
        if(debugLogs) Debug.Log($"[Recovery] Mission Started for {busID}. Path Length: {m_ServerCurrentLegLength:F1}m");
    }

    private void CancelMission(string reason)
    {
        Debug.LogWarning($"[Recovery] Mission Cancelled: {reason}");
        _netState.Value = new RecoveryNetworkState { CurrentState = RecoveryState.Idle };
    }

    private void FinishMission()
    {
        if(debugLogs) Debug.Log("[Recovery] Mission Complete. Now Idle and Ready.");
        _netState.Value = new RecoveryNetworkState { CurrentState = RecoveryState.Idle };
        if (_ownerDepot != null) _ownerDepot.OnRecoveryVehicleFinished();
    }

    private void Update()
    {
        // Teleport Guard
        if (Vector3.Distance(transform.position, _lastFramePos) > 10.0f && _lastFramePos != Vector3.zero)
        {
            if (debugLogs) Debug.LogWarning($"[Recovery] Teleport detected! Moved {Vector3.Distance(transform.position, _lastFramePos):F1}m. State: {_netState.Value.CurrentState}");
        }
        _lastFramePos = transform.position;

        if (IsServer) ServerUpdateLoop();
        if (IsClient) ClientUpdateLoop();
    }

    private void ServerUpdateLoop()
    {
        var state = _netState.Value;
        if (state.CurrentState == RecoveryState.Idle) return;

        float dt = Time.deltaTime * SimulationTimeManager.Instance.TimeMultiplier;

        if (state.CurrentState == RecoveryState.MovingToTarget || state.CurrentState == RecoveryState.Returning)
        {
            if (_serverIsMoving)
            {
                float trafficModifier = 1.0f;
                if (GridManager.Instance != null && m_ServerPathSegments.Count > 0)
                {
                    Vector3 serverPos = CalculatePoint(m_ServerDistanceTraveled, m_ServerPathSegments, out _);
                    trafficModifier = GridManager.Instance.GetTrafficModifierAt(serverPos);
                }

                m_ServerDistanceTraveled += baseSpeed * trafficModifier * dt;
                
                if (m_ServerDistanceTraveled >= m_ServerCurrentLegLength)
                {
                    _serverIsMoving = false;
                    ServerOnDestinationReached(state);
                }
            }
        }
        else if (state.CurrentState == RecoveryState.Repairing)
        {
            ServerHandleRepair(dt);
        }
        else if (state.CurrentState == RecoveryState.Refilling)
        {
            ServerHandleRefill(dt);
        }
    }

    private void ServerOnDestinationReached(RecoveryNetworkState state)
    {
        if (state.CurrentState == RecoveryState.MovingToTarget)
        {
            var newState = state;
            newState.CurrentState = RecoveryState.Repairing;
            _netState.Value = newState;
        }
        else if (state.CurrentState == RecoveryState.Returning)
        {
            if(debugLogs) Debug.Log("[Recovery] Returned to Depot. Starting Refill.");
            _currentRefillTimer = refillDuration;
            
            var newState = state;
            newState.CurrentState = RecoveryState.Refilling;
            _netState.Value = newState;
        }
    }

    private void ServerHandleRefill(float dt)
    {
        if (_ownerDepot != null)
        {
            transform.position = _ownerDepot.SpawnNode.transform.position;
            transform.rotation = _ownerDepot.SpawnNode.transform.rotation;
        }

        _currentRefillTimer -= dt;
        if (_currentRefillTimer <= 0)
        {
            FinishMission();
        }
    }

    private void ServerHandleRepair(float dt)
    {
        if (_targetBusData == null) 
            _targetBusData = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == _netState.Value.TargetBusID.ToString());

        float targetHealth = MaintenanceManager.Instance.operationalThreshold;
        
        if (_targetBusData != null && _targetBusData.Durability < targetHealth)
        {
            float boost = repairRatePerSecond * dt;
            FleetManager.Instance.UpdateBusDurability(_targetBusData.BusID, _targetBusData.Durability + boost);
        }
        else
        {
            GameObject busObj = FleetManager.Instance.GetActiveBus(_targetBusData.BusID);
            if (busObj != null) busObj.GetComponent<BusDriver>()?.SetBrokenDown(false);
            ServerStartReturnTrip();
        }
    }

    private void ServerStartReturnTrip()
    {
        var state = _netState.Value;
        if (_targetSegmentCache == null) 
            _targetSegmentCache = FindSegmentByName(state.TargetSegmentName.ToString());

        if (_targetSegmentCache == null) { FinishMission(); return; }

        bool wasHeadingToB = state.EnteringFromNodeA;
        RoadNode exitNode = wasHeadingToB ? _targetSegmentCache.NodeB : _targetSegmentCache.NodeA;
        RoadNode homeNode = _ownerDepot.SpawnNode;

        var nodePath = RoadPathfinder.FindPath(exitNode, homeNode);
        
        if (nodePath == null) 
        { 
            if(debugLogs) Debug.LogError("[Recovery] Cannot find return path!");
            //if (homeNode != null) transform.position = homeNode.transform.position;
            FinishMission(); 
            return; 
        }

        m_ServerPathSegments.Clear();
        m_ServerCurrentLegLength = 0f;

        float startT = state.TargetSplineT;
        float endT = wasHeadingToB ? 1f : 0f;
        
        AddPathLeg(_targetSegmentCache, startT, endT, m_ServerPathSegments, ref m_ServerCurrentLegLength);
        ConvertNodesToSegments(nodePath, m_ServerPathSegments, ref m_ServerCurrentLegLength);

        _netState.Value = new RecoveryNetworkState
        {
            CurrentState = RecoveryState.Returning,
            OwnerDepotID = state.OwnerDepotID,
            TargetBusID = "", 
            TargetSegmentName = state.TargetSegmentName,
            TargetSplineT = state.TargetSplineT,
            EnteringFromNodeA = state.EnteringFromNodeA,
            DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay
        };

        m_ServerDistanceTraveled = 0f;
        _serverIsMoving = true;
    }

    // --- CLIENT LOGIC ---

    private void OnStateChanged(RecoveryNetworkState oldState, RecoveryNetworkState newState)
    {
        if (newState.CurrentState == RecoveryState.Idle)
        {
            m_ClientIsMoving = false;
            return;
        }

        if (newState.CurrentState == RecoveryState.Repairing || newState.CurrentState == RecoveryState.Refilling)
        {
            m_ClientIsMoving = false;
            return;
        }

        if (newState.CurrentState == RecoveryState.MovingToTarget || newState.CurrentState == RecoveryState.Returning)
        {
            RebuildClientPath(newState);
            
            float timeMult = SimulationTimeManager.Instance.TimeMultiplier > 0 ? SimulationTimeManager.Instance.TimeMultiplier : 1f;
            float gameHoursPassed = SimulationTimeManager.Instance.CurrentTimeOfDay - newState.DepartureTime;
            if (gameHoursPassed < 0) gameHoursPassed += 24f;
            float realSecondsPassed = (gameHoursPassed * 60f) / (SimulationTimeManager.Instance.baseMinutesPerSecond * timeMult);

            m_ClientDistanceTraveled = realSecondsPassed * baseSpeed * clientSpeedBuffer;
            m_ClientIsMoving = true;
        }
    }

    private void RebuildClientPath(RecoveryNetworkState state)
    {
        m_LocalPathSegments.Clear();
        m_TotalLegLength = 0f;

        DepotController depot = GetClientDepot(state.OwnerDepotID.ToString());
        RoadSegment targetSegment = FindSegmentByName(state.TargetSegmentName.ToString());

        if (depot == null || targetSegment == null) return;

        if (state.CurrentState == RecoveryState.MovingToTarget)
        {
            RoadNode startNode = depot.SpawnNode;
            
            var nodePath = RoadPathfinder.FindPathToSegment(startNode, targetSegment);

            ConvertNodesToSegments(nodePath, m_LocalPathSegments, ref m_TotalLegLength);
            float startT = state.EnteringFromNodeA? 0f : 1f;
            float endT = state.TargetSplineT;

            AddPathLeg(targetSegment, startT, endT, m_LocalPathSegments, ref m_TotalLegLength);
        }
        else if (state.CurrentState == RecoveryState.Returning)
        {
            bool exitToNodeB = state.EnteringFromNodeA;
            RoadNode exitNode = exitToNodeB ? targetSegment.NodeB : targetSegment.NodeA;
            RoadNode endNode = depot.SpawnNode;

            float startT = state.TargetSplineT;
            float endT = exitToNodeB ? 1f : 0f;
            AddPathLeg(targetSegment, startT, endT, m_LocalPathSegments, ref m_TotalLegLength);

            var nodePath = RoadPathfinder.FindPath(exitNode, endNode);
            ConvertNodesToSegments(nodePath, m_LocalPathSegments, ref m_TotalLegLength);
        }
    }

    private void ClientUpdateLoop()
    {
        // copied from bus driver
       if (!m_ClientIsMoving || m_LocalPathSegments == null || m_LocalPathSegments.Count == 0) return;

        float dt = Time.deltaTime * SimulationTimeManager.Instance.TimeMultiplier;

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

        UpdateTransformOnSpline(m_ClientDistanceTraveled, m_LocalPathSegments);
    }

    // --- HELPERS ---

    
    
    private void BuildServerPathToTarget(List<RoadNode> nodes, RoadSegment finalSeg, bool enterFromA, float targetT)
    {
        m_ServerPathSegments.Clear();
        m_ServerCurrentLegLength = 0f;
        ConvertNodesToSegments(nodes, m_ServerPathSegments, ref m_ServerCurrentLegLength);
        float startT = enterFromA ? 0f : 1f;
        AddPathLeg(finalSeg, startT, targetT, m_ServerPathSegments, ref m_ServerCurrentLegLength);
    }

    private void ConvertNodesToSegments(List<RoadNode> nodes, List<PathLeg> pathList, ref float totalLen)
    {
        if (nodes == null || nodes.Count < 2) return;
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
                    AddPathLeg(seg, tStart, tEnd, pathList, ref totalLen);
                    break;
                }
            }
        }
    }

    private RoadSegment FindSegmentByName(string name)
    {
        var segments = FindObjectsByType<RoadSegment>(FindObjectsSortMode.None);
        return segments.FirstOrDefault(s => s.name == name);
    }

    private DepotController GetClientDepot(string id)
    {
        if (_clientDepotCache == null) _clientDepotCache = new Dictionary<string, DepotController>();
        if (_clientDepotCache.TryGetValue(id, out var cached)) return cached;
        var depots = FindObjectsByType<DepotController>(FindObjectsSortMode.None);
        var found = depots.FirstOrDefault(d => d.depotID == id);
        if (found != null) _clientDepotCache[id] = found;
        return found;
    }

}