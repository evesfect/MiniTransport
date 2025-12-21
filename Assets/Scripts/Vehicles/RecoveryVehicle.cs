using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

public class RecoveryVehicle : VehicleDriver
{
    public enum RecoveryState { Idle, MovingToTarget, Repairing, Returning }

    [Header("Recovery Settings")]
    public float repairRatePerSecond = 10f;

    [Header("State")]
    private NetworkVariable<RecoveryState> _currentState = new NetworkVariable<RecoveryState>(RecoveryState.Idle);
    private string _targetBusID;
    private Transform _homeDepot;
    private BusData _targetBusData;
    private Vector3 _exactTargetPos;

    // Helper to track path completion since base class splits Server/Client distance
    private bool _hasArrived => m_ServerDistanceTraveled >= m_ServerCurrentLegLength;

    public bool IsBusy => _currentState.Value != RecoveryState.Idle;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            _currentState.Value = RecoveryState.Idle;
        }
    }

    public void StartMission(string busID, Transform depot)
    {
        if (!IsServer) return;

        _targetBusID = busID;
        _homeDepot = depot;
        _targetBusData = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == busID);

        GameObject busObj = FleetManager.Instance.GetActiveBus(busID);
        if (busObj == null || _targetBusData == null)
        {
            Debug.LogError($"[Recovery] Cannot find bus {busID}");
            _currentState.Value = RecoveryState.Idle;
            return;
        }

        // 1. Plan Path to Bus
        if (PlanPath(transform.position, busObj.transform.position))
        {
            _exactTargetPos = busObj.transform.position;
            _currentState.Value = RecoveryState.MovingToTarget;
        }
        else
        {
            // Fallback: Teleport if no path found
            Debug.LogWarning("[Recovery] No path found. Teleporting (Fallback).");
            transform.position = busObj.transform.position;
            _currentState.Value = RecoveryState.Repairing;
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        // --- PAUSE CHECK ---
        float timeMult = (SimulationTimeManager.Instance != null) ? SimulationTimeManager.Instance.TimeMultiplier : 1f;

        // Don't update if paused
        if (timeMult <= 0.001f) return;

        switch (_currentState.Value)
        {
            case RecoveryState.MovingToTarget:
                HandleMovement(timeMult, () => {
                    Debug.Log("[Recovery] Arrived at bus. Starting repairs.");
                    _currentState.Value = RecoveryState.Repairing;
                });
                break;

            case RecoveryState.Repairing:
                HandleRepair(timeMult);
                break;

            case RecoveryState.Returning:
                HandleMovement(timeMult, () => {
                    Debug.Log("[Recovery] Returned to depot.");
                    _currentState.Value = RecoveryState.Idle;
                    MaintenanceManager.Instance.OnRecoveryVehicleFinished();
                });
                break;
        }
    }

    // --- MOVEMENT LOGIC (Uses VehicleDriver) ---

    private void HandleMovement(float timeMult, System.Action onComplete)
    {
        if (m_ServerPathSegments == null || m_ServerPathSegments.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        // 1. Increment Distance
        float step = baseSpeed * Time.deltaTime * timeMult;
        m_ServerDistanceTraveled += step;

        // 2. Update Transform using Base Class Method
        // Note: We pass the list because the base method requires it
        UpdateTransformOnSpline(m_ServerDistanceTraveled, m_ServerPathSegments);

        // 3. Check Completion
        if (_hasArrived)
        {
            // Snap to exact target (covers the gap from last road node to actual object)
            if (_currentState.Value == RecoveryState.MovingToTarget || _currentState.Value == RecoveryState.Returning)
            {
                transform.position = _exactTargetPos;
            }
            onComplete?.Invoke();
        }
    }

    private bool PlanPath(Vector3 startPos, Vector3 endPos)
    {
        RoadNode startNode = FindNearestNode(startPos);
        RoadNode endNode = FindNearestNode(endPos);

        if (startNode == null || endNode == null) return false;

        List<RoadNode> nodePath = RoadPathfinder.FindPath(startNode, endNode);

        if (nodePath == null || nodePath.Count < 2) return false;

        // Reset Base Class Data
        m_ServerPathSegments.Clear();
        m_ServerDistanceTraveled = 0f;
        m_ServerCurrentLegLength = 0f;

        // Convert Nodes to PathLegs using Base Class Helper
        for (int i = 0; i < nodePath.Count - 1; i++)
        {
            RoadNode nA = nodePath[i];
            RoadNode nB = nodePath[i + 1];

            foreach (var seg in nA.ConnectedRoads)
            {
                if (seg.GetConnectedNode(nA) == nB)
                {
                    float tStart = (seg.NodeA == nA) ? 0f : 1f;
                    float tEnd = (seg.NodeA == nA) ? 1f : 0f;

                    // Uses VehicleDriver.AddPathLeg
                    AddPathLeg(seg, tStart, tEnd, m_ServerPathSegments, ref m_ServerCurrentLegLength);
                    break;
                }
            }
        }

        return m_ServerPathSegments.Count > 0;
    }

    // --- REPAIR LOGIC ---

    private void HandleRepair(float timeMult)
    {
        if (_targetBusData == null)
        {
            ReturnHome();
            return;
        }

        float targetHealth = MaintenanceManager.Instance.operationalThreshold;
        float current = _targetBusData.Durability;

        if (current < targetHealth)
        {
            float boost = repairRatePerSecond * Time.deltaTime * timeMult;
            FleetManager.Instance.UpdateBusDurability(_targetBusID, current + boost);
        }
        else
        {
            // Repair Complete
            GameObject busObj = FleetManager.Instance.GetActiveBus(_targetBusID);
            if (busObj != null)
            {
                var driver = busObj.GetComponent<BusDriver>();
                if (driver != null) driver.SetBrokenDown(false);
            }

            ReturnHome();
        }
    }

    private void ReturnHome()
    {
        if (PlanPath(transform.position, _homeDepot.position))
        {
            _exactTargetPos = _homeDepot.position;
            _currentState.Value = RecoveryState.Returning;
        }
        else
        {
            transform.position = _homeDepot.position;
            _currentState.Value = RecoveryState.Idle;
            MaintenanceManager.Instance.OnRecoveryVehicleFinished();
        }
    }

    private RoadNode FindNearestNode(Vector3 pos)
    {
        RoadNode[] allNodes = FindObjectsOfType<RoadNode>();
        RoadNode best = null;
        float minDst = float.MaxValue;

        foreach (var node in allNodes)
        {
            float d = Vector3.Distance(node.transform.position, pos);
            if (d < minDst)
            {
                minDst = d;
                best = node;
            }
        }
        return best;
    }
}