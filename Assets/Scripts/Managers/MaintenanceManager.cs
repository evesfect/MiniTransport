using UnityEngine;
using System.Linq;
using Unity.Netcode;

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
        if (FleetManager.Instance == null) return;

        // 1. Repair Inactive Buses (In Depot)
        foreach (var busData in FleetManager.Instance.allBuses)
        {
            if (!FleetManager.Instance.IsBusActive(busData.BusID))
            {
                if (busData.Durability < 100f)
                {
                    float newDurability = busData.Durability + repairRatePerHour;
                    FleetManager.Instance.UpdateBusDurability(busData.BusID, newDurability);
                    Debug.Log($"BusID: {busData.BusID}, Durability: {newDurability}");
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
                Debug.Log($"[Maintenance] Bus {busID} has broken down!");
            }
        }
    }
}