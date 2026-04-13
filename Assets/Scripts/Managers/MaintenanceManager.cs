using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;
using static Unity.Burst.Intrinsics.X86.Avx;

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
            // Only repair buses in depot
            if (FleetManager.Instance.IsBusActive(busData.BusID)) continue;
            if (busData.Parts == null) continue;

            string assignedDepot = busData.AssignedDepotID;
            if (string.IsNullOrEmpty(assignedDepot) || !depotRepairPower.ContainsKey(assignedDepot)) continue;

            float repairBudget = depotRepairPower[assignedDepot] * repairPerSkillPoint;
            
            foreach (var part in busData.Parts)
            {
                if (repairBudget <= 0) break;

                // STRATEGY: REPLACE OR REPAIR?
                
                // A. REPLACEMENT (If MaxLife is too low)
                if (part.MaxLife < replacePartThreshold)
                {
                    string itemID = GetItemIDForPart(part.PartType);

                    // Check Inventory
                    if (InventoryManager.Instance.GetItemQuantity(itemID) > 0)
                    {
                        // Consume Item
                        InventoryManager.Instance.DecreaseItemQuantity(itemID, 1);

                        // Reset Part to Brand New
                        FleetManager.Instance.UpdateBusPartMaxLife(busData.BusID, part.PartType, 100f);
                        FleetManager.Instance.UpdateBusPartHealth(busData.BusID, part.PartType, 100f);

                        Debug.Log($"[Maintenance] Replaced {part.PartType} on {busData.BusID}");
                        continue; // Done with this part
                    }
                }

                // B. REPAIR (If not replacing, or no item available)
                if (part.Health < part.MaxLife)
                {
                    Debug.Log("Test");
                    float needed = part.MaxLife - part.Health;
                    float applied = Mathf.Min(needed, repairBudget);

                    float newHealth = part.Health + applied;
                    if (newHealth > part.MaxLife) newHealth = part.MaxLife;
                    Debug.Log($"[Maintenance] Repaired {part.PartType}");
                    FleetManager.Instance.UpdateBusPartHealth(busData.BusID, part.PartType, newHealth);
                    repairBudget -= applied;
                }
            }
        }
    }
    private void TriggerBreakdown(string busID, BusPartType reason)
    {
        GameObject busObj = FleetManager.Instance.GetActiveBus(busID);
        if (busObj != null)
        {
            BusDriver driver = busObj.GetComponent<BusDriver>();
            if (driver != null)
            {
                driver.SetBrokenDown(true, reason);
                Debug.Log($"[Maintenance] Bus {busID} Broken Down. Reason: {reason}. Adding to Queue.");

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
