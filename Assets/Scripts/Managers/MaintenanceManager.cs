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
    public float repairRatePerHour = 10f;

    private List<string> _breakdownList = new List<string>();

   
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
                    TriggerBreakdown(busData);
                    Debug.Log($"Breakdown triggered for BUSID: {busData.BusID}");
                }
            }
        }
    }

    private void OnHourTick()
    {
        if (FleetManager.Instance == null) return;

        // 1. Repair Inactive Buses (In Depot)
        foreach (var busData in FleetManager.Instance.allBuses)
        {
            if (!FleetManager.Instance.IsBusActive(busData.BusID) && busData.Durability < 100f)
            {
                             
                float newDurability = busData.Durability + repairRatePerHour;
                FleetManager.Instance.UpdateBusDurability(busData.BusID, newDurability);
                Debug.Log($"BusID: {busData.BusID}, Durability: {newDurability}");
                
            }
        }
    }

    private void TriggerBreakdown(BusData busData)
    {
        GameObject busObj = FleetManager.Instance.GetActiveBus(busData.BusID);
        if (busObj != null)
        {
            BusDriver driver = busObj.GetComponent<BusDriver>();
            if (driver != null)
            {
                driver.SetBrokenDown(true);
                Debug.Log($"[Maintenance] Bus {busData.BusID} Broken Down. Adding to Queue.");

                if (!_breakdownList.Contains(busData.BusID))
                {
                    _breakdownList.Add(busData.BusID);
                    TryDispatchJobs();
                }
            }
        }
    }

    // --- JOB DISPATCHING ---

    // Called whenever a new breakdown happens OR a depot reports it's free
    public void TryDispatchJobs()
    {
        if (_breakdownList.Count == 0) return;

        // Iterate backwards so we can remove items safely
        for (int i = _breakdownList.Count - 1; i >= 0; i--)
        {
            string busID = _breakdownList[i];

            // 1. Find which Depot owns this bus
            var busData = FleetManager.Instance.allBuses.FirstOrDefault(b => b.BusID == busID);
            if (busData == null)
            {
                _breakdownList.RemoveAt(i);
                continue;
            }

            DepotController assignedDepot = FindDepotByID(busData.AssignedDepotID);

            // 2. Check if that Depot is ready
            if (assignedDepot != null && assignedDepot.IsRecoveryAvailable)
            {
                Debug.Log($"[Maintenance] Dispatching Job for {busID} to {assignedDepot.depotID}");

                // 3. Remove from queue
                _breakdownList.RemoveAt(i);

                // 4. Command the Depot to start
                assignedDepot.DispatchRecoveryVehicle(busID);

                
            }
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
