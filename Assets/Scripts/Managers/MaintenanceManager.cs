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
                    part.Health -= decayRatePerMinute * decayMult;
                    part.MaxLife -= maxLifeDecayRate * decayMult;

                    if (part.MaxLife < 10f) part.MaxLife = 10f; // Minimum structural integrity                 
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
                bool needsRepair = busData.Parts.Any(p => p.Health < p.MaxLife || p.MaxLife < replacePartThreshold);
                if (needsRepair)
                {
                    var worstPart = busData.Parts.OrderBy(p => p.Health).First();
                    UpsertDepotWorkItem(busData.BusID, worstPart.PartType, WorkItemStatus.AwaitingTechnician, "Unassigned");
                }
                continue;
            }

            float repairBudget = depotRepairPower[assignedDepot] * repairPerSkillPoint;
            string mechanicName = GetAssignedMechanic(assignedDepot);

            // Track the most critical part we can't service this tick
            BusPartType? blockingPart = null;
            WorkItemStatus blockingStatus = WorkItemStatus.AwaitingParts;

            foreach (var part in busData.Parts)
            {
                if (repairBudget <= 0) break;

                // A. REPLACEMENT (If MaxLife is too low)
                if (part.MaxLife < replacePartThreshold)
                {
                    string itemID = GetItemIDForPart(part.PartType);
                    if (InventoryManager.Instance.GetItemQuantity(itemID) > 0)
                    {
                        InventoryManager.Instance.DecreaseItemQuantity(itemID, 1);
                        FleetManager.Instance.UpdateBusPartMaxLife(busData.BusID, part.PartType, 100f);
                        FleetManager.Instance.UpdateBusPartHealth(busData.BusID, part.PartType, 100f);
                        Debug.Log($"[Maintenance] Replaced {part.PartType} on {busData.BusID}");
                        continue;
                    }
                    else
                    {
                        // Record only the most critical blocking part (lowest enum value = most critical)
                        if (blockingPart == null || (int)part.PartType < (int)blockingPart.Value)
                        {
                            blockingPart = part.PartType;
                            blockingStatus = WorkItemStatus.AwaitingParts;
                        }
                        continue;
                    }
                }

                // B. REPAIR
                if (part.Health < part.MaxLife)
                {
                    float needed = part.MaxLife - part.Health;
                    float applied = Mathf.Min(needed, repairBudget);
                    FleetManager.Instance.UpdateBusPartHealth(busData.BusID, part.PartType, Mathf.Min(part.Health + applied, part.MaxLife));
                    repairBudget -= applied;
                    Debug.Log($"[Maintenance] Repaired {part.PartType} on {busData.BusID}");
                }
            }

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

    private string GetItemIDForPart(BusPartType type)
    {
        switch (type)
        {
            case BusPartType.Engine: return "engine_block";
            case BusPartType.Transmission: return "gearbox_std";
            case BusPartType.Wheels: return "tire_standard";
            case BusPartType.Body: return "body_panel";
            case BusPartType.Interior: return "seat_fabric";
            default: return "generic_part";
        }
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
