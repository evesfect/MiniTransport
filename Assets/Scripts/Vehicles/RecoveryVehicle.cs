using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Collections.Generic;
using System.Linq;

// 1. Define the Network State Struct
public enum RecoveryState { Idle, MovingToTarget, ApproachingTarget, Repairing, ReturningToRoad, Returning }

public struct RecoveryNetworkState : INetworkSerializable, System.IEquatable<RecoveryNetworkState>
{
    public RecoveryState CurrentState;
    public Vector3 StartPos;
    public Vector3 TargetPos;
    public float DepartureTime;     
    public FixedString32Bytes TargetBusID;
    public FixedString32Bytes OwnerDepotID;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref CurrentState);
        serializer.SerializeValue(ref StartPos);
        serializer.SerializeValue(ref TargetPos);
        serializer.SerializeValue(ref DepartureTime);
        serializer.SerializeValue(ref TargetBusID);
        serializer.SerializeValue(ref OwnerDepotID);
    }

    public bool Equals(RecoveryNetworkState other)
    {
        return CurrentState == other.CurrentState &&
               StartPos == other.StartPos &&
               TargetPos == other.TargetPos &&
               DepartureTime == other.DepartureTime &&
               TargetBusID == other.TargetBusID &&
               OwnerDepotID == other.OwnerDepotID;
    }
}

public class RecoveryVehicle : VehicleDriver
{
    [Header("Recovery Settings")]
    public float repairRatePerSecond = 10f;

    // --- Network State ---
    private readonly NetworkVariable<RecoveryNetworkState> _netState = new NetworkVariable<RecoveryNetworkState>(
        new RecoveryNetworkState { CurrentState = RecoveryState.Idle },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // --- Server-Side Data ---
    private DepotController _ownerDepot;
    private BusData _targetBusData;

    // Server Ghost Simulation
    private bool _serverIsMoving;

    private RoadNode _cachedTargetNode;

    public bool IsBusy => _netState.Value.CurrentState != RecoveryState.Idle;

    public override void OnNetworkSpawn()
    {
        _netState.OnValueChanged += OnStateChanged;

        // Initial sync for clients joining late
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
        

        if (busObj == null || _targetBusData == null)
        {
            Debug.LogError($"[Recovery] Cannot find bus {busID}");
            // Reset to idle if failed
            _netState.Value = new RecoveryNetworkState { CurrentState = RecoveryState.Idle };
            return;
        }

        RoadNode startNode = _ownerDepot.SpawnNode;
        RoadNode endNode = GetClosestRoadNode(busObj.transform.position);

        if (startNode == null || endNode == null)
        {
            Debug.LogError("[Recovery] Cannot find nodes for path.");
            _netState.Value = new RecoveryNetworkState { CurrentState = RecoveryState.Idle };
            return;
        }

        if (PlanServerPath(startNode, endNode))
        {
            _netState.Value = new RecoveryNetworkState
            {
                CurrentState = RecoveryState.MovingToTarget,
                StartPos = transform.position,
                TargetPos = busObj.transform.position, // Actual Bus Position
                DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay,
                TargetBusID = busID,
                OwnerDepotID = depot.depotID
            };

            m_ServerDistanceTraveled = 0f;
            _serverIsMoving = true;

        }
        else
        {
            // Fallback: Teleport
            transform.position = busObj.transform.position;
            _netState.Value = new RecoveryNetworkState { CurrentState = RecoveryState.Repairing, TargetBusID = busID, OwnerDepotID = depot.depotID };
        }
    }

    private void FinishMission()
    {
        _netState.Value = new RecoveryNetworkState { CurrentState = RecoveryState.Idle };
        if (_ownerDepot != null) _ownerDepot.OnRecoveryVehicleFinished();
    }

    private void Update()
    {
        if (IsServer) ServerUpdateLoop();
        if (IsClient) ClientUpdateLoop();
    }

    private void ServerUpdateLoop()
    {
        var state = _netState.Value;
        float dt = Time.deltaTime * SimulationTimeManager.Instance.TimeMultiplier;

        // --- OFF-ROAD MOVEMENT HANDLERS ---
        if (state.CurrentState == RecoveryState.ReturningToRoad)
        {
            // Gap: Broken Bus -> Road Node
            MoveLinearlyToTarget(state.TargetPos, dt, () => {
                // Arrived at Node, start Highway Return
                RoadNode node = GetClosestRoadNode(transform.position);
                ServerStartHighwayReturn(node);
            });
            return;
        }

        if (state.CurrentState == RecoveryState.ApproachingTarget)
        {
            // Gap: Road Node -> Broken Bus
            MoveLinearlyToTarget(state.TargetPos, dt, () => {
                // Arrived at Bus, start Repairing
                var newState = state;
                newState.CurrentState = RecoveryState.Repairing;
                _netState.Value = newState;
            });
            return;
        }

        // --- ROAD MOVEMENT HANDLERS ---
        if (state.CurrentState == RecoveryState.MovingToTarget || state.CurrentState == RecoveryState.Returning)
        {
            if (_serverIsMoving)
            {
                m_ServerDistanceTraveled += baseSpeed * dt;
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
    }

    private void ServerOnDestinationReached(RecoveryNetworkState state)
    {
        transform.position = state.TargetPos;

        if (state.CurrentState == RecoveryState.MovingToTarget)
        {
            _netState.Value = new RecoveryNetworkState
            {
                CurrentState = RecoveryState.ApproachingTarget,
                StartPos = transform.position,
                TargetPos = state.TargetPos, // Bus Location
                DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay,
                TargetBusID = state.TargetBusID,
                OwnerDepotID = state.OwnerDepotID
            };
        }
        else if (state.CurrentState == RecoveryState.Returning)
        {
            FinishMission();
        }
    }

    private void ServerHandleRepair(float dt)
    {
        string busID = _netState.Value.TargetBusID.ToString();
        if (_targetBusData == null) _targetBusData = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == busID);

        float targetHealth = MaintenanceManager.Instance.operationalThreshold;
        if (_targetBusData != null && _targetBusData.Durability < targetHealth)
        {
            float boost = repairRatePerSecond * dt;
            FleetManager.Instance.UpdateBusDurability(busID, _targetBusData.Durability + boost);
        }
        else
        {
            // Fix Completed
            GameObject busObj = FleetManager.Instance.GetActiveBus(busID);
            if (busObj != null) busObj.GetComponent<BusDriver>()?.SetBrokenDown(false);
            ServerReturnHome();
        }
    }

    private void ServerReturnHome()
    {
        RoadNode startNode = GetClosestRoadNode(transform.position);

        if (startNode != null)
        {
            // Enter "ReturningToRoad" state: Drive straight to the node
            _netState.Value = new RecoveryNetworkState
            {
                CurrentState = RecoveryState.ReturningToRoad,
                StartPos = transform.position,
                TargetPos = startNode.transform.position,
                DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay,
                TargetBusID = "",
                OwnerDepotID = _ownerDepot.depotID
            };

            m_ServerDistanceTraveled = 0f;
            _serverIsMoving = true;
        }
        else
        {
            // Fallback if no node found nearby
            FinishMission();
        }
    }

    private void ServerStartHighwayReturn(RoadNode startNode)
    {
        RoadNode homeNode = _ownerDepot.SpawnNode;

        if (homeNode != null && PlanServerPath(startNode, homeNode))
        {
            _netState.Value = new RecoveryNetworkState
            {
                CurrentState = RecoveryState.Returning,
                StartPos = startNode.transform.position,
                TargetPos = homeNode.transform.position,
                DepartureTime = SimulationTimeManager.Instance.CurrentTimeOfDay,
                TargetBusID = "",
                OwnerDepotID = _ownerDepot.depotID
            };

            m_ServerDistanceTraveled = 0f;
            _serverIsMoving = true;
        }
        else
        {
            FinishMission();
        }
    }

    // --- CLIENT LOGIC ---

    private void OnStateChanged(RecoveryNetworkState oldState, RecoveryNetworkState newState)
    {
        if (newState.CurrentState == RecoveryState.Idle)
        {
            m_ClientIsMoving = false;
            return;
        }

        if (newState.CurrentState == RecoveryState.Repairing)
        {
            m_ClientIsMoving = false;
            // Snap to bus
            GameObject bus = FleetManager.Instance.GetActiveBus(newState.TargetBusID.ToString());
            if (bus != null) transform.position = bus.transform.position;
            return;
        }

        if (newState.CurrentState == RecoveryState.ReturningToRoad || newState.CurrentState == RecoveryState.ApproachingTarget)
        {
            
            m_ClientIsMoving = true;
            return;
        }

        if (newState.CurrentState == RecoveryState.MovingToTarget || newState.CurrentState == RecoveryState.Returning)
        {
            RoadNode depotNode = GetDepotNode(newState.OwnerDepotID.ToString());


            if (newState.CurrentState == RecoveryState.MovingToTarget)
            {
                // 1. Find the same Node the server used (Closest to TargetPos)
                RoadNode busNode = GetClosestRoadNode(newState.TargetPos);

                if (depotNode != null && busNode != null)
                {
                    // 2. Plan Path: Depot -> Closest Node
                    PlanLocalPath(depotNode, busNode);
                }
            }
            
            else
            {
                // Driving from StartPos (The Node we drove to) -> Depot
                RoadNode startNode = GetClosestRoadNode(newState.StartPos);
                if (startNode != null && depotNode != null)
                {
                    PlanLocalPath(startNode, depotNode);
                }
            }

            // Sync Time
            float timeMult = SimulationTimeManager.Instance.TimeMultiplier > 0 ? SimulationTimeManager.Instance.TimeMultiplier : 1f;
            float gameHoursPassed = SimulationTimeManager.Instance.CurrentTimeOfDay - newState.DepartureTime;
            if (gameHoursPassed < 0) gameHoursPassed += 24f;
            float realSecondsPassed = (gameHoursPassed * 60f) / (SimulationTimeManager.Instance.baseMinutesPerSecond * timeMult);

            m_ClientDistanceTraveled = realSecondsPassed * baseSpeed * clientSpeedBuffer;
            m_ClientIsMoving = true;
        }
    }

    private void ClientUpdateLoop()
    {
        if (!m_ClientIsMoving) return;
        float dt = Time.deltaTime * SimulationTimeManager.Instance.TimeMultiplier;

        // 1. Handle Off-Road States
        var state = _netState.Value;
        if (state.CurrentState == RecoveryState.ReturningToRoad || state.CurrentState == RecoveryState.ApproachingTarget)
        {
            Vector3 target = state.TargetPos;

            transform.position = Vector3.MoveTowards(transform.position, target, baseSpeed * clientSpeedBuffer * dt);

            Vector3 dir = target - transform.position;
            if (dir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);
            }
            return;
        }

        // 2. Handle Road States
        if (m_LocalPathSegments.Count == 0) return;

        m_ClientDistanceTraveled += baseSpeed * clientSpeedBuffer * dt;

        if (m_ClientDistanceTraveled >= m_TotalLegLength)
        {
            m_ClientDistanceTraveled = m_TotalLegLength;
            m_ClientIsMoving = false;
        }

        UpdateTransformOnSpline(m_ClientDistanceTraveled, m_LocalPathSegments);
    }

    // --- HELPERS ---

    private bool PlanServerPath(RoadNode startNode, RoadNode endNode)
    {
        m_ServerPathSegments.Clear();
        m_ServerCurrentLegLength = 0f;
        var nodePath = RoadPathfinder.FindPath(startNode, endNode);
        if (nodePath == null || nodePath.Count < 2) return false;
        ConvertNodesToSegments(nodePath, m_ServerPathSegments, ref m_ServerCurrentLegLength);
        return m_ServerPathSegments.Count > 0;
    }

    private void PlanLocalPath(RoadNode startNode, RoadNode endNode)
    {
        m_LocalPathSegments.Clear();
        m_TotalLegLength = 0f;
        var nodePath = RoadPathfinder.FindPath(startNode, endNode);
        if (nodePath == null) return;
        ConvertNodesToSegments(nodePath, m_LocalPathSegments, ref m_TotalLegLength);
    }

    private void MoveLinearlyToTarget(Vector3 target, float dt, System.Action onComplete)
    {
        float dist = Vector3.Distance(transform.position, target);
        float step = baseSpeed * dt;

        if (step >= dist)
        {
            transform.position = target;
            onComplete?.Invoke();
        }
        else
        {
            transform.position = Vector3.MoveTowards(transform.position, target, step);
            transform.LookAt(target);
        }
    }

    private void ConvertNodesToSegments(List<RoadNode> nodes, List<PathLeg> pathList, ref float totalLen)
    {
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

    private RoadNode GetDepotNode(string depotID)
    {
        // Use FindObjectsByType instead of the obsolete FindObjectsOfType
        var depots = FindObjectsByType<DepotController>(FindObjectsSortMode.None);
        var depot = depots.FirstOrDefault(d => d.depotID == depotID);
        return depot != null ? depot.SpawnNode : null;
    }

    //Expensive Method(Consider using the grid)
    private RoadNode GetClosestRoadNode(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(pos, 50f);
        RoadNode bestNode = null;
        float closestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            var node = hit.GetComponent<RoadNode>();
            if (node != null)
            {
                float d = Vector3.Distance(pos, node.transform.position);
                if (d < closestDist) { closestDist = d; bestNode = node; }
            }
        }
        return bestNode;
    }
}