using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-45)] // Run after TimeManager/FleetManager, before Depot
public class MaintenanceManager : NetworkBehaviour
{
    public static MaintenanceManager Instance { get; private set; }

    [Header("Settings")]
    public float operationalThreshold = 30f; // Min durability to leave depot (X)
    public float breakdownThreshold = 5f;    // Durability where bus stops (Y)
    public float replacePartThreshold = 20f;

    [Tooltip("Durability lost per in-game minute while driving")]
    public float decayRatePerMinute = 0.5f;
    public float maxLifeDecayRate = 0.05f;

    [Tooltip("Durability gained per in-game hour while in depot")]
    public float repairPerSkillPoint = 2.0f;

    private readonly List<WorkItem> _workItems = new List<WorkItem>();
    private readonly HashSet<string> _breakdownSet = new HashSet<string>(); // Fast duplicate check for on-route breakdowns

    public IReadOnlyList<WorkItem> WorkQueue => _workItems;
    public event Action OnWorkQueueChanged;


    [Header("Capacity & Prioritization")]
    [Tooltip("The order in which mechanics will attempt to fix parts.")]
    public List<BusPartType> repairPriority = new List<BusPartType>
    {
        BusPartType.Engine,
        BusPartType.Transmission,
        BusPartType.Wheels,
        BusPartType.Body,
        BusPartType.Interior
    };

    public float GetMaxCapacityAllowance(BusPartType type)
    {
        return type switch
        {
            BusPartType.Engine => 50f,
            BusPartType.Transmission => 40f,
            BusPartType.Wheels => 20f,
            BusPartType.Body => 20f,
            BusPartType.Interior => 10f,
            _ => 10f,
        };
    }

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

    // --- NETWORKED SETTINGS SYNC ---

    [Rpc(SendTo.Server)]
    public void UpdateThresholdsRpc(float operational, float replace)
    {
        operationalThreshold = operational;
        replacePartThreshold = replace;
    }

    [Rpc(SendTo.Server)]
    public void UpdateRepairPriorityRpc(BusPartType[] newPriority)
    {
        // Convert the array back to a List and save it
        repairPriority = new List<BusPartType>(newPriority);
        Debug.Log("[Maintenance] Priority list reordered by player!");
    }

    private void OnMinuteTick()
    {
        if (FleetManager.Instance == null) return;

        foreach (var busData in FleetManager.Instance.allBuses)
        {
            // Ensure parts exist (Migration helper)
            if (busData.Parts == null || busData.Parts.Count == 0) busData.InitializeParts();

            GameObject busObj = FleetManager.Instance.GetActiveBus(busData.BusID);

            // Only decay active buses
            if (busObj != null)
            {
                BusDriver driver = busObj.GetComponent<BusDriver>();
                if (driver == null || driver.IsBroken) continue;

                // Decay each part
                foreach (var part in busData.Parts)
                {
                    // Decay Health
                    
                    float decayMult = GetDecayMultiplier(part.PartType);

                    part.MaxLife -= maxLifeDecayRate * decayMult;
                    part.Health -= decayRatePerMinute * decayMult;


                    if (part.MaxLife < 10f) part.MaxLife = 10f; // Minimum structural integrity                 
                    if (part.Health > part.MaxLife) part.Health = part.MaxLife;
                    if (part.Health < 0f) part.Health = 0f;

                    if (part.Health <= breakdownThreshold)
                    {
                        
                        // Only critical parts stop the bus immediately
                        if (IsCriticalPart(part.PartType))
                        {
                            TriggerBreakdown(busData.BusID, part.PartType);
                            break; // Stop checking other parts for this bus
                        }
                    }
                }
            }
        }
    }

    private void OnHourTick()
    {
        if (FleetManager.Instance == null || EmployeeManager.Instance == null) return;

        // 1. Calculate repair power per depot
        Dictionary<string, float> depotRepairPower = new Dictionary<string, float>();
        foreach (var emp in EmployeeManager.Instance.allEmployees)
        {
            if (emp.Role == EmployeeRole.Mechanic && !string.IsNullOrEmpty(emp.AssignedDepotID))
            {
                if (!depotRepairPower.ContainsKey(emp.AssignedDepotID))
                    depotRepairPower[emp.AssignedDepotID] = 0f;
                depotRepairPower[emp.AssignedDepotID] += emp.SkillLevel;
            }
        }

        Debug.Log($"[Maintenance] Hour Tick. Found {depotRepairPower.Count} active depots with mechanics.");

        // 2. Repair inactive buses and update work items for depot-parked buses
        foreach (var busData in FleetManager.Instance.allBuses)
        {
            if (FleetManager.Instance.IsBusActive(busData.BusID)) continue;
            if (busData.Parts == null) continue;

            string assignedDepot = busData.AssignedDepotID;
            if (string.IsNullOrEmpty(assignedDepot)) continue;

            bool hasMechanic = depotRepairPower.ContainsKey(assignedDepot) && depotRepairPower[assignedDepot] > 0f;

            if (!hasMechanic)
            {
                // Unchanged: Handle unassigned mechanic logic
                bool needsRepair = busData.Parts.Any(p => p.Health < p.MaxLife || p.MaxLife < replacePartThreshold);
                if (needsRepair)
                {
                    var worstPart = busData.Parts.OrderBy(p => p.Health).First();
                    UpsertDepotWorkItem(busData.BusID, worstPart.PartType, WorkItemStatus.AwaitingTechnician, "Unassigned");
                    Debug.Log($"[Maintenance] Bus {busData.BusID} needs repair but Depot {assignedDepot} has no mechanics!");
                }
                continue;
            }



            // THE NEW BANDWIDTH LOGIC
            float availableCapacity = depotRepairPower[assignedDepot];
            string mechanicName = GetAssignedMechanic(assignedDepot);

            BusPartType? blockingPart = null;
            WorkItemStatus blockingStatus = WorkItemStatus.AwaitingParts;

            foreach (var part in busData.Parts)
            {
                if (part.Health > part.MaxLife) part.Health = part.MaxLife;
            }

            // Filter to only parts that need work, then sort them by our Priority List
            var partsNeedingWork = busData.Parts
                .Where(p => p.Health < p.MaxLife || p.MaxLife < replacePartThreshold)
                .OrderBy(p => repairPriority.IndexOf(p.PartType))
                .ToList();

            if (partsNeedingWork.Count > 0)
            {
                Debug.Log($"[Maintenance] Bus {busData.BusID} has {partsNeedingWork.Count} parts needing work. Depot Capacity available: {availableCapacity:F1}");
            }

            foreach (var part in partsNeedingWork)
            {
                // If the depot is out of bandwidth for this hour, stop working!
                if (availableCapacity <= 0) break;

                // Apply the Bottleneck: How much effort can actually go into this part right now?
                float maxAllowance = GetMaxCapacityAllowance(part.PartType);
                float allocatedCapacity = Mathf.Min(availableCapacity, maxAllowance);

                // A. REPLACEMENT LOGIC
                if (part.MaxLife < replacePartThreshold)
                {
                    // 1. DIAGNOSIS: If the bus doesn't know what part it needs yet, roll a random one!
                    if (string.IsNullOrEmpty(part.PendingReplacementItemID))
                    {
                        string[] acceptableItemIDs = GetValidItemIDsForPart(part.PartType);

                        // Pick a random index from the array
                        int randomIndex = UnityEngine.Random.Range(0, acceptableItemIDs.Length);
                        part.PendingReplacementItemID = acceptableItemIDs[randomIndex];

                        Debug.Log($"[Maintenance] DIAGNOSIS: Bus {busData.BusID}'s {part.PartType} has failed. Mechanic demands a '{part.PendingReplacementItemID}' to fix it.");

                        // Note: You may want to call a FleetManager RPC here to sync this new string to clients so the UI updates!
                    }

                    string requiredItemID = part.PendingReplacementItemID;
                    float consumedDurability = 0f;

                    // 2. Try to consume that EXACT required part
                    if (InventoryManager.Instance.TryConsumeItem(requiredItemID, out consumedDurability))
                    {
                        // We found the exact part! Reset the stats
                        FleetManager.Instance.UpdateBusPartMaxLife(busData.BusID, part.PartType, consumedDurability);
                        FleetManager.Instance.UpdateBusPartHealth(busData.BusID, part.PartType, consumedDurability);

                        // CLEAR THE DIAGNOSIS so the next time it breaks, it can ask for something else
                        part.PendingReplacementItemID = "";

                        availableCapacity -= allocatedCapacity;
                        Debug.Log($"[Maintenance] REPLACED {part.PartType} on {busData.BusID} using the required '{requiredItemID}'. Consumed {allocatedCapacity:F1} capacity. {availableCapacity:F1} remaining.");
                        continue;
                    }
                    else
                    {
                        // 3. We don't have the specific part in stock. Block the repair queue.
                        if (blockingPart == null || repairPriority.IndexOf(part.PartType) < repairPriority.IndexOf(blockingPart.Value))
                        {
                            blockingPart = part.PartType;
                            blockingStatus = WorkItemStatus.AwaitingParts;
                        }

                        Debug.Log($"[Maintenance] Blocked: Need to replace {part.PartType} on {busData.BusID}. Waiting for delivery of '{requiredItemID}'.");
                        continue;
                    }
                }

                // B. REPAIR LOGIC
                if (part.Health < part.MaxLife)
                {
                    float missingHealth = part.MaxLife - part.Health;
                    float potentialHeal = allocatedCapacity * repairPerSkillPoint;

                    if (potentialHeal >= missingHealth)
                    {
                        // We have more than enough capacity to finish the job
                        float capacityUsed = missingHealth / repairPerSkillPoint;
                        availableCapacity -= capacityUsed; // Only consume what we actually used

                        FleetManager.Instance.UpdateBusPartHealth(busData.BusID, part.PartType, part.MaxLife);
                        Debug.Log($"[Maintenance] FULLY REPAIRED {part.PartType} on {busData.BusID}. Healed {missingHealth:F1}. Consumed {capacityUsed:F1} capacity. {availableCapacity:F1} left.");
                    }
                    else
                    {
                        // We maxed out our allowance for this hour, so it's a partial heal
                        availableCapacity -= allocatedCapacity; // Consume the full allocated capacity

                        FleetManager.Instance.UpdateBusPartHealth(busData.BusID, part.PartType, part.Health + potentialHeal);
                        Debug.Log($"[Maintenance] PARTIALLY REPAIRED {part.PartType} on {busData.BusID}. Healed {potentialHeal:F1}. Consumed {allocatedCapacity:F1} capacity. 0 left.");
                    }
                }
            }

            depotRepairPower[assignedDepot] = availableCapacity;

            // Upsert a single work item for the most critical blocking part
            if (blockingPart.HasValue)
                UpsertDepotWorkItem(busData.BusID, blockingPart.Value, blockingStatus, mechanicName);

            // Remove depot work item once all parts are healthy
            bool allHealthy = busData.Parts.All(p => p.Health >= p.MaxLife && p.MaxLife >= replacePartThreshold);
            if (allHealthy)
                RemoveDepotWorkItem(busData.BusID);
        }
    }
    private void TriggerBreakdown(string busID, BusPartType reason)
    {
        GameObject busObj = FleetManager.Instance.GetActiveBus(busID);
        if (busObj == null) return;

        BusDriver driver = busObj.GetComponent<BusDriver>();
        if (driver == null) return;

        driver.SetBrokenDown(true, reason);
        Debug.Log($"[Maintenance] Bus {busID} Broken Down. Reason: {reason}. Adding to Queue.");

        if (!_breakdownSet.Contains(busID))
        {
            _breakdownSet.Add(busID);
            var item = new WorkItem(busID, reason, WorkItemStatus.AwaitingTechnician);
            item.Priority = _workItems.Count;
            _workItems.Add(item);
            OnWorkQueueChanged?.Invoke();
            TryDispatchJobs();
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
        var pendingItems = _workItems
            .Where(w => w.Status == WorkItemStatus.AwaitingTechnician && _breakdownSet.Contains(w.BusID))
            .OrderBy(w => w.Priority)
            .ToList();

        foreach (var item in pendingItems)
        {
            GameObject busObj = FleetManager.Instance.GetActiveBus(item.BusID);
            if (busObj == null) continue;

            BusDriver driver = busObj.GetComponent<BusDriver>();
            if (driver != null && !driver.IsFullyStopped) continue;

            var busData = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == item.BusID);
            if (busData == null) continue;

            DepotController assignedDepot = FindDepotByID(busData.AssignedDepotID);
            if (assignedDepot != null && assignedDepot.IsRecoveryAvailable)
            {
                Debug.Log($"[Maintenance] Dispatching Job for {item.BusID} to {assignedDepot.depotID}");
                assignedDepot.DispatchRecoveryVehicle(item.BusID);
                item.Status = WorkItemStatus.InRepair;
                item.AssignedTechnicianName = "Field Crew";
                item.EstimatedCompletionLabel = "In Progress";
                OnWorkQueueChanged?.Invoke();
            }
        }
    }

    public void OnDepotFree(string depotID)
    {
        TryDispatchJobs();
    }

    // --- WORK QUEUE API ---

    public void PrioritizeWorkItem(string workItemID)
    {
        var item = _workItems.FirstOrDefault(w => w.WorkItemID == workItemID);
        if (item == null) return;

        _workItems.Remove(item);
        _workItems.Insert(0, item);

        for (int i = 0; i < _workItems.Count; i++)
            _workItems[i].Priority = i;

        OnWorkQueueChanged?.Invoke();

        if (item.Status == WorkItemStatus.AwaitingTechnician)
            TryDispatchJobs();
    }

    public void RemoveWorkItem(string busID)
    {
        int removed = _workItems.RemoveAll(w => w.BusID == busID);
        _breakdownSet.Remove(busID);
        if (removed > 0)
            OnWorkQueueChanged?.Invoke();
    }

    public void ReorderWorkQueue(List<string> workItemIDs)
    {
        var reordered = new List<WorkItem>(workItemIDs.Count);
        foreach (var id in workItemIDs)
        {
            var item = _workItems.FirstOrDefault(w => w.WorkItemID == id);
            if (item != null) reordered.Add(item);
        }
        // Append anything not covered (safety)
        foreach (var item in _workItems)
            if (!reordered.Contains(item)) reordered.Add(item);

        _workItems.Clear();
        _workItems.AddRange(reordered);

        for (int i = 0; i < _workItems.Count; i++)
            _workItems[i].Priority = i;

        OnWorkQueueChanged?.Invoke();
    }

    // --- PRIVATE HELPERS ---

    private void UpsertDepotWorkItem(string busID, BusPartType partType, WorkItemStatus status, string techName)
    {
        // One depot work item per bus (non-breakdown items)
        var existing = _workItems.FirstOrDefault(w => w.BusID == busID && !_breakdownSet.Contains(busID));
        if (existing != null)
        {
            existing.IssuePartType = partType;
            existing.Status = status;
            existing.AssignedTechnicianName = techName;
            existing.EstimatedCompletionLabel = status == WorkItemStatus.AwaitingParts ? "Pending Delivery" : "No Mechanic Assigned";
        }
        else
        {
            var item = new WorkItem(busID, partType, status);
            item.Priority = _workItems.Count;
            item.AssignedTechnicianName = techName;
            item.EstimatedCompletionLabel = status == WorkItemStatus.AwaitingParts ? "Pending Delivery" : "No Mechanic Assigned";
            _workItems.Add(item);
        }
        OnWorkQueueChanged?.Invoke();
    }

    private void RemoveDepotWorkItem(string busID)
    {
        int removed = _workItems.RemoveAll(w => w.BusID == busID && !_breakdownSet.Contains(busID));
        if (removed > 0)
            OnWorkQueueChanged?.Invoke();
    }

    private string GetAssignedMechanic(string depotID)
    {
        if (EmployeeManager.Instance == null) return "Unassigned";
        var mechanic = EmployeeManager.Instance.allEmployees
            .FirstOrDefault(e => e.Role == EmployeeRole.Mechanic && e.AssignedDepotID == depotID);
        return mechanic != null ? mechanic.FullName : "Unassigned";
    }

    private DepotController FindDepotByID(string depotID)
    {
        var depots = FindObjectsByType<DepotController>(FindObjectsSortMode.None);
        return depots.FirstOrDefault(d => d.depotID == depotID);
    }

    // HELPERS

    private string[] GetValidItemIDsForPart(BusPartType type)
    {
        // Returns an array of acceptable item IDs that can fulfill the replacement requirement
        return type switch
        {
            BusPartType.Engine => new[] { "EngineBlock", "Piston", "Alternator" },
            BusPartType.Transmission => new[] { "Axle", "BusFrame" }, 
            BusPartType.Wheels => new[] { "StandardTire", "WinterTire", "HeavyDutyTire" },
            BusPartType.Body => new[] { "BusFrame", "DoorAssembly" },
            BusPartType.Interior => new[] { "Dashboard", "SensorArray", "WiringHarness" },
            _ => new[] { "generic_part" }
        };
    }

    private bool IsCriticalPart(BusPartType type)
    {
        return type == BusPartType.Engine || type == BusPartType.Transmission || type == BusPartType.Wheels;
    }


    private float GetDecayMultiplier(BusPartType type)
    {
        switch (type)
        {
            case BusPartType.Wheels: return 1.2f; // Tires wear out fast
            case BusPartType.Body: return 0.2f;   // Body lasts long
            default: return 1.0f;
        }
    }
}
