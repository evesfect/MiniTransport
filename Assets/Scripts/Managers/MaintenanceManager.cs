using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;

[DefaultExecutionOrder(-45)] // Run after TimeManager/FleetManager, before Depot
public class MaintenanceManager : NetworkBehaviour
{
    public static MaintenanceManager Instance { get; private set; }

    [Header("Settings")]
    public float operationalThreshold = 30f; // Min durability to leave depot (X)
    public float breakdownThreshold = 5f;    // Durability where bus stops (Y)
    
    [Tooltip("Durability lost per in-game minute while driving")]
    public float decayRatePerMinute = 0.2f;  

    [Tooltip("Durability gained per in-game hour while in depot")]
    public float repairPerSkillPoint = 0.2f;

    private List<string> _breakdownList = new List<string>();
    private Queue<string> _breakdownQueue = new Queue<string>();
    private HashSet<string> _breakdownSet = new HashSet<string>();

   
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && SimulationTimeManager.Instance != null)
        {
            SimulationTimeManager.Instance.OnMinuteChanged += OnMinuteTick;
            SimulationTimeManager.Instance.OnHourChanged += OnHourTick;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && SimulationTimeManager.Instance != null)
        {
            SimulationTimeManager.Instance.OnMinuteChanged -= OnMinuteTick;
            SimulationTimeManager.Instance.OnHourChanged -= OnHourTick;
        }
    }

    private void OnMinuteTick()
    {
        if (FleetManager.Instance == null) return;

        // 1. Decay Active Buses
        foreach (var busData in FleetManager.Instance.allBuses)
        {
            GameObject busObj = FleetManager.Instance.GetActiveBus(busData.BusID);

            if (busObj != null)
            {
                BusDriver driver = busObj.GetComponent<BusDriver>();

                if (driver == null || driver.IsBroken){ continue; }
                // Apply Decay
                float newDurability = busData.Durability - decayRatePerMinute;
                FleetManager.Instance.UpdateBusDurability(busData.BusID, newDurability);
                // Debug.Log($"Durability to BUSID: {busData.BusID}, durability: {newDurability}");
                // Check Breakdown
                if (newDurability < breakdownThreshold)
                {
                    TriggerBreakdown(busData.BusID);
                    Debug.Log($"Breakdown triggered for BUSID: {busData.BusID}");
                }
            }
        }
    }

    private void OnHourTick()
    {
        if (FleetManager.Instance == null || EmployeeManager.Instance == null) return;

        // 1. Calculate Repair Power for EACH Depot
        // Dictionary: Key = DepotID, Value = Total Mechanic Skill
        Dictionary<string, float> depotRepairPower = new Dictionary<string, float>();

        foreach (var emp in EmployeeManager.Instance.allEmployees)
        {
            // We only care about Mechanics who are assigned to a valid depot
            if (emp.Role == EmployeeRole.Mechanic && !string.IsNullOrEmpty(emp.AssignedDepotID))
            {
                if (!depotRepairPower.ContainsKey(emp.AssignedDepotID))
                {
                    depotRepairPower[emp.AssignedDepotID] = 0f;
                }

                // Add this mechanic's skill to their depot's total
                depotRepairPower[emp.AssignedDepotID] += emp.SkillLevel;
            }
        }

        // 2. Repair Inactive Buses based on THEIR Assigned Depot
        foreach (var busData in FleetManager.Instance.allBuses)
        {
            // Only repair buses that are sitting in the depot (Inactive)
            if (!FleetManager.Instance.IsBusActive(busData.BusID))
            {
                // Don't repair if already at 100%
                if (busData.Durability >= 100f) continue;

                // CHECK: Which depot is this bus assigned to?
                string assignedDepot = busData.AssignedDepotID;

                // If bus has no depot, nobody repairs it
                if (string.IsNullOrEmpty(assignedDepot)) continue;

                // Find the repair power for THIS specific depot
                float totalSkillInDepot = 0f;
                if (depotRepairPower.TryGetValue(assignedDepot, out float power))
                {
                    totalSkillInDepot = power;
                }

                // Calculate repair amount
                // If totalSkillInDepot is 0 (no mechanics), repairAmount is 0
                float repairAmount = totalSkillInDepot * repairPerSkillPoint;

                if (repairAmount > 0)
                {
                    float newDurability = Mathf.Min(100f, busData.Durability + repairAmount);
                    FleetManager.Instance.UpdateBusDurability(busData.BusID, newDurability);

                    // Debug.Log($"[Maintenance] Repaired {busData.BusID} at {assignedDepot}. Amount: {repairAmount:F1}");
                }
            }
        }
    }
    private void TriggerBreakdown(string busID)
    {
        GameObject busObj = FleetManager.Instance.GetActiveBus(busID);
        if (busObj != null)
        {
            BusDriver driver = busObj.GetComponent<BusDriver>();
            if (driver != null)
            {
                driver.SetBrokenDown(true);
                Debug.Log($"[Maintenance] Bus {busID} Broken Down. Adding to Queue.");

                if (!_breakdownSet.Contains(busID))
                {
                    _breakdownQueue.Enqueue(busID);
                    _breakdownSet.Add(busID);
                    TryDispatchJobs();
                }
            }
        }
    }

    // --- JOB DISPATCHING ---

    public void OnBusStopped(string busID)
    {
        Debug.Log($"[Maintenance] Bus {busID} reported fully stopped. Attempting dispatch.");
        TryDispatchJobs(); 
    }
    public void TryDispatchJobs()
    {
        if (_breakdownQueue.Count == 0) return;

        List<string> requeue = new List<string>();

        while (_breakdownQueue.Count > 0)
        {
            string busID = _breakdownQueue.Dequeue();
            _breakdownSet.Remove(busID);

            GameObject busObj = FleetManager.Instance.GetActiveBus(busID);
            if (busObj == null) { continue;}
            
            BusDriver driver = busObj.GetComponent<BusDriver>();
            
            if (driver != null && !driver.IsFullyStopped)
            {
                requeue.Add(busID);
                continue;
            }

            var busData = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == busID);
            DepotController assignedDepot = FindDepotByID(busData.AssignedDepotID);

            if (assignedDepot != null && assignedDepot.IsRecoveryAvailable)
            {
                Debug.Log($"[Maintenance] Dispatching Job for {busID} to {assignedDepot.depotID}");
                assignedDepot.DispatchRecoveryVehicle(busID);
            } else
            {
                requeue.Add(busID);
            }
        }
        foreach (string busID in requeue)
        {
            _breakdownQueue.Enqueue(busID);
            _breakdownSet.Add(busID);
        }
    }

    public void OnDepotFree(string depotID)
    {
        TryDispatchJobs();
    }

    private DepotController FindDepotByID(string depotID)
    {
        
        var depots = FindObjectsByType<DepotController>(FindObjectsSortMode.None);
        return depots.FirstOrDefault(d => d.depotID == depotID);
    }

}
