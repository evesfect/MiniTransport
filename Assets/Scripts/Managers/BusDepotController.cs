using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class DepotController : MonoBehaviour
{
    [Header("Identity")]
    public string depotID = "Depot_Main";

    [Header("Fleet Configuration")]
    public GameObject busPrefab;
    
    // This list is populated at runtime from FleetManager
    [SerializeField] // For debugging only
    private List<DepotBusEntry> fleet = new List<DepotBusEntry>();

    [Header("Debug")]
    [SerializeField] private bool _debugLogs = true;

    private void Start()
    {
        // 1. Fetch Data from Manager
        if (FleetManager.Instance != null)
        {
            fleet = FleetManager.Instance.GetBusesForDepot(depotID);
            if(_debugLogs) Debug.Log($"Depot '{depotID}' initialized with {fleet.Count} buses.");
        }
        else
        {
            Debug.LogError("FleetManager missing! Depot cannot initialize fleet.");
        }

        // 2. Subscribe to Time
        if (SimulationTimeManager.Instance != null)
        {
            SimulationTimeManager.Instance.OnMinuteChanged += CheckSchedules;
        }
        
        CheckSchedules();
    }

    private void OnDestroy()
    {
        if (SimulationTimeManager.Instance != null)
        {
            SimulationTimeManager.Instance.OnMinuteChanged -= CheckSchedules;
        }
    }
    
    public void CheckSchedules()
    {
        if (fleet == null) return;
        
        float currentTime = SimulationTimeManager.Instance.CurrentTimeOfDay;

        foreach (var entry in fleet)
        {
            if (entry.CurrentState == BusState.InDepot)
            {
                if (currentTime >= entry.Schedule.StartTime && currentTime < entry.Schedule.EndTime)
                {
                    SpawnBus(entry);
                }
            }
        }
    }

    private void SpawnBus(DepotBusEntry entry)
    {
        if (busPrefab == null) return;
        if (TransportManager.Instance == null) return;

        Route route = TransportManager.Instance.ActiveRoutes.FirstOrDefault(r => r.RouteID == entry.Schedule.RouteID);
        if (route == null || route.StopIDs.Count == 0) return;

        BusStop startStop = TransportManager.Instance.GetStop(route.StopIDs[0]);
        if (startStop == null) return;

        Vector3 spawnPos = startStop.transform.position;
        Quaternion spawnRot = startStop.transform.rotation; 
        
        if(startStop.parentSegment != null)
        {
            spawnPos = startStop.parentSegment.GetPointOnRoad(startStop.splineT, true); 
        }

        GameObject newBusObj = Instantiate(busPrefab, spawnPos, spawnRot);
        newBusObj.name = $"{entry.BusID}_({route.RouteName})";

        BusDriver driver = newBusObj.GetComponent<BusDriver>();
        if (driver == null) driver = newBusObj.AddComponent<BusDriver>();

        driver.Initialize(entry, this);

        entry.ActiveBusInstance = newBusObj;
        entry.CurrentState = BusState.OnRoute;

        if (_debugLogs) 
        {
            float time = SimulationTimeManager.Instance.CurrentTimeOfDay;
            Debug.Log($"Depot: Spawned {entry.BusID} on route {route.RouteName} at {time:F2}");
        }
    }

    public void ReturnBusToDepot(DepotBusEntry entry)
    {
        if (entry.ActiveBusInstance != null)
        {
            Destroy(entry.ActiveBusInstance);
        }

        entry.ActiveBusInstance = null;
        entry.CurrentState = BusState.InDepot;
        
        if (_debugLogs) Debug.Log($"Depot: Bus {entry.BusID} returned to depot.");
    }
}