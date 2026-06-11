using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-45)] // Run after TimeManager/FleetManager, before Depot
public class MaintenanceManager : NetworkBehaviour
{

    private class TeamCapacity
    {
        public string TeamID;
        public string DepotID;
        public float AvailableCapacity;
        public string LeadMechanicName;
        public bool IsCurrentlyWorkingOnABus;
    }

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

    // --- KPI collection hooks (server-side; consumed by KPIManager) ---
    public event Action<string, BusPartType> OnBreakdownOccurred; // busID, failed part
    public event Action OnRepairCompleted;                        // a part fully repaired
    public event Action OnPartReplaced;                           // a part swapped from inventory
    public bool IsOnRouteBreakdown(string busID)
    {
        return _breakdownSet.Contains(busID);
    }


    [Header("Capacity & Prioritization")]
    [Tooltip("The order in which mechanics will attempt to fix parts.")]
    public List<BusPartType> repairPriority = new List<BusPartType>
    {
        BusPartType.Engine,
        BusPartType.Transmission,
        BusPartType.Tires,
        BusPartType.Body,
        BusPartType.Interior
    };

    public float GetMaxCapacityAllowance(BusPartType type)
    {
        return type switch
        {
            BusPartType.Engine => 50f,
            BusPartType.Transmission => 40f,
            BusPartType.Tires => 20f,
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

        // 1. Group active mechanics by their composite key: (DepotID + TeamID)
        Dictionary<string, TeamCapacity> activeTeams = new Dictionary<string, TeamCapacity>();

        foreach (var emp in EmployeeManager.Instance.allEmployees)
        {
            if (emp.Role == EmployeeRole.Mechanic && !string.IsNullOrEmpty(emp.AssignedDepotID))
            {
                string teamName = string.IsNullOrEmpty(emp.AssignedTeamID) ? "General Crew" : emp.AssignedTeamID;
                string teamKey = $"{emp.AssignedDepotID}_{teamName}";

                if (!activeTeams.ContainsKey(teamKey))
                {
                    activeTeams[teamKey] = new TeamCapacity
                    {
                        TeamID = teamName,
                        DepotID = emp.AssignedDepotID,
                        AvailableCapacity = 0f,
                        LeadMechanicName = emp.FullName, // First mechanic acts as the team's lead/display name
                        IsCurrentlyWorkingOnABus = false
                    };
                }
                // Pool team skill levels together
                activeTeams[teamKey].AvailableCapacity += emp.SkillLevel;
            }
        }

        Debug.Log($"[Maintenance] Hour Tick. Found {activeTeams.Count} distinct active teams across all depots.");

        // 2. Process parked buses needing repairs concurrently
        foreach (var busData in FleetManager.Instance.allBuses)
        {
            if (FleetManager.Instance.IsBusActive(busData.BusID)) continue;
            if (busData.Parts == null) continue;

            string assignedDepot = busData.AssignedDepotID;
            if (string.IsNullOrEmpty(assignedDepot)) continue;

            // Ensure health values remain clamped securely
            foreach (var part in busData.Parts)
            {
                if (part.Health > part.MaxLife) part.Health = part.MaxLife;
            }

            // Check if any parts require intervention
            bool needsWork = busData.Parts.Any(p => p.Health < p.MaxLife || p.MaxLife < replacePartThreshold);
            if (!needsWork)
            {
                RemoveDepotWorkItem(busData.BusID);
                continue;
            }

            // Find an available team within this depot that isn't already assigned to another bus this hour
            TeamCapacity assignedTeam = activeTeams.Values.FirstOrDefault(t =>
                t.DepotID == assignedDepot &&
                !t.IsCurrentlyWorkingOnABus &&
                t.AvailableCapacity > 0f);

            if (assignedTeam == null)
            {
                // No unassigned teams remain available to process this bus right now
                var worstPart = busData.Parts.OrderBy(p => p.Health).First();
                UpsertDepotWorkItem(busData.BusID, worstPart.PartType, WorkItemStatus.AwaitingTechnician, "No Free Team");
                continue;
            }

            // Lock this team onto this bus for the duration of the current hour tick
            assignedTeam.IsCurrentlyWorkingOnABus = true;
            string displayTeamLabel = $"{assignedTeam.TeamID} ({assignedTeam.LeadMechanicName})";

            BusPartType? blockingPart = null;
            WorkItemStatus blockingStatus = WorkItemStatus.AwaitingParts;

            // Sort broken parts strictly by our defined repair priority sequence
            var partsNeedingWork = busData.Parts
                .Where(p => p.Health < p.MaxLife || p.MaxLife < replacePartThreshold)
                .OrderBy(p => repairPriority.IndexOf(p.PartType))
                .ToList();

            if (partsNeedingWork.Count > 0)
            {
                Debug.Log($"[Maintenance] Bus {busData.BusID} assigned to {assignedTeam.TeamID}. Available Capacity: {assignedTeam.AvailableCapacity:F1}");
            }

            foreach (var part in partsNeedingWork)
            {
                // If the assigned team exhausts its bandwidth for the hour, pause work
                if (assignedTeam.AvailableCapacity <= 0) break;

                // Apply specialization modifiers to the capacity bottleneck allowance
                
                float maxAllowance = GetMaxCapacityAllowance(part.PartType);

                float allocatedCapacity = Mathf.Min(assignedTeam.AvailableCapacity, maxAllowance);

                // A. REPLACEMENT LOGIC
                if (part.MaxLife < replacePartThreshold)
                {
                    if (string.IsNullOrEmpty(part.PendingReplacementItemID))
                    {
                        string[] acceptableItemIDs = GetValidItemIDsForPart(part.PartType);
                        part.PendingReplacementItemID = acceptableItemIDs[UnityEngine.Random.Range(0, acceptableItemIDs.Length)];
                        Debug.Log($"[Maintenance] DIAGNOSIS: Bus {busData.BusID}'s {part.PartType} failed. {assignedTeam.TeamID} demands '{part.PendingReplacementItemID}'.");
                    }

                    string requiredItemID = part.PendingReplacementItemID;

                    if (InventoryManager.Instance.TryConsumeItem(requiredItemID, out float consumedDurability))
                    {
                        FleetManager.Instance.UpdateBusPartMaxLife(busData.BusID, part.PartType, consumedDurability);
                        FleetManager.Instance.UpdateBusPartHealth(busData.BusID, part.PartType, consumedDurability);
                        part.PendingReplacementItemID = "";

                        assignedTeam.AvailableCapacity -= allocatedCapacity;
                        Debug.Log($"[Maintenance] REPLACED {part.PartType} on {busData.BusID} using '{requiredItemID}'. {assignedTeam.AvailableCapacity:F1} capacity left.");
                        OnPartReplaced?.Invoke(); // KPI: count parts replaced
                        continue;
                    }
                    else
                    {
                        // Parts out of stock isolate the bottleneck to this specific team only
                        if (blockingPart == null || repairPriority.IndexOf(part.PartType) < repairPriority.IndexOf(blockingPart.Value))
                        {
                            blockingPart = part.PartType;
                            blockingStatus = WorkItemStatus.AwaitingParts;
                        }
                        Debug.Log($"[Maintenance] Blocked: {assignedTeam.TeamID} waiting for delivery of '{requiredItemID}' for Bus {busData.BusID}.");
                        continue;
                    }
                }

                // B. REPAIR LOGIC
                if (part.Health < part.MaxLife)
                {
                    float missingHealth = part.MaxLife - part.Health;
                    float effectiveRepairRate = repairPerSkillPoint;
                    float potentialHeal = allocatedCapacity * effectiveRepairRate;

                    if (potentialHeal >= missingHealth)
                    {
                        float capacityUsed = missingHealth / effectiveRepairRate;
                        assignedTeam.AvailableCapacity -= capacityUsed;
                        FleetManager.Instance.UpdateBusPartHealth(busData.BusID, part.PartType, part.MaxLife);
                        Debug.Log($"[Maintenance] FULLY REPAIRED {part.PartType} on {busData.BusID} by +{potentialHeal:F1} HP using {assignedTeam.TeamID}. {assignedTeam.AvailableCapacity:F1} left.");
                        OnRepairCompleted?.Invoke(); // KPI: count repairs completed
                    }
                    else
                    {
                        assignedTeam.AvailableCapacity -= allocatedCapacity;
                        FleetManager.Instance.UpdateBusPartHealth(busData.BusID, part.PartType, part.Health + potentialHeal);
                        Debug.Log($"[Maintenance] PARTIALLY REPAIRED {part.PartType} on {busData.BusID} by +{potentialHeal:F1} HP using {assignedTeam.TeamID}. 0 capacity left.");
                    }
                }
            }

            // Update UI Work Item attributed directly to the active team
            if (blockingPart.HasValue)
            {
                UpsertDepotWorkItem(busData.BusID, blockingPart.Value, blockingStatus, displayTeamLabel);
            }
            else
            {
                bool allHealthy = busData.Parts.All(p => p.Health >= p.MaxLife && p.MaxLife >= replacePartThreshold);
                if (allHealthy)
                {
                    RemoveDepotWorkItem(busData.BusID);
                }
                else
                {
                    UpsertDepotWorkItem(busData.BusID, partsNeedingWork.First().PartType, WorkItemStatus.InRepair, displayTeamLabel);
                }
            }
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
            OnBreakdownOccurred?.Invoke(busID, reason); // KPI: count lifetime breakdowns
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

    public void ClearDepotWorkItemForActiveBus(string busID)
    {
        int removed = _workItems.RemoveAll(w => w.BusID == busID && !_breakdownSet.Contains(busID));
        if (removed > 0)
        {
            OnWorkQueueChanged?.Invoke();
        }
    }

    // --- PRIVATE HELPERS ---

    private void UpsertDepotWorkItem(string busID, BusPartType partType, WorkItemStatus status, string techName)
    {
        string completionLabel = status switch
        {
            WorkItemStatus.AwaitingParts => "Pending Delivery",
            WorkItemStatus.InRepair => "In Repair",
            WorkItemStatus.AwaitingTechnician => "Awaiting Team",
            _ => "No Mechanic Assigned"
        };

        // Allow updating existing breakdown items instead of ignoring them
        var existing = _workItems.FirstOrDefault(w => w.BusID == busID);
        if (existing != null)
        {
            existing.IssuePartType = partType;
            existing.Status = status;
            existing.AssignedTechnicianName = techName;
            existing.EstimatedCompletionLabel = completionLabel;
        }
        else
        {
            var item = new WorkItem(busID, partType, status);
            item.Priority = _workItems.Count;
            item.AssignedTechnicianName = techName;
            item.EstimatedCompletionLabel = completionLabel;
            _workItems.Add(item);
        }

        OnWorkQueueChanged?.Invoke();
    }

    private void RemoveDepotWorkItem(string busID)
    {
        // Remove all work items for this bus since it is fully repaired
        int removed = _workItems.RemoveAll(w => w.BusID == busID);

        // Clear the bus from the breakdown tracking set so it doesn't block future maintenance checks
        if (_breakdownSet.Contains(busID))
        {
            _breakdownSet.Remove(busID);
            removed++;
        }

        if (removed > 0)
        {
            OnWorkQueueChanged?.Invoke();
        }
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
            BusPartType.Tires => new[] { "StandardTire", "WinterTire", "HeavyDutyTire" },
            BusPartType.Body => new[] { "BusFrame", "DoorAssembly" },
            BusPartType.Interior => new[] { "Dashboard", "SensorArray", "WiringHarness" },
            _ => new[] { "generic_part" }
        };
    }

    private bool IsCriticalPart(BusPartType type)
    {
        return type == BusPartType.Engine || type == BusPartType.Transmission || type == BusPartType.Tires;
    }


    private float GetDecayMultiplier(BusPartType type)
    {
        switch (type)
        {
            case BusPartType.Tires: return 1.2f; // Tires wear out fast
            case BusPartType.Body: return 0.2f;   // Body lasts long
            default: return 1.0f;
        }
    }
}
