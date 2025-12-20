using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class DepotController : NetworkBehaviour
{
    [Header("Identity")]
    public string depotID = "Depot_Main";

    [Header("Fleet Configuration")]
    public GameObject busPrefab;

    public override void OnNetworkSpawn()
    {
        if (IsServer && SimulationTimeManager.Instance != null)
        {
            SimulationTimeManager.Instance.OnMinuteChanged += CheckSchedules;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && SimulationTimeManager.Instance != null)
        {
            SimulationTimeManager.Instance.OnMinuteChanged -= CheckSchedules;
        }
    }

    // Main Loop: Iterate global list, filter for this depot, act on state
    public void CheckSchedules()
    {
        if (FleetManager.Instance == null) return;
        float threshold = MaintenanceManager.Instance != null ? MaintenanceManager.Instance.operationalThreshold : 0f;

        float currentTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        var myBuses = FleetManager.Instance.allBuses.Where(b => b.AssignedDepotID == depotID);

        foreach (var busData in myBuses)
        {
            bool shouldBeActive = currentTime >= busData.Schedule.StartTime && currentTime < busData.Schedule.EndTime;
            bool isCurrentlyActive = FleetManager.Instance.IsBusActive(busData.BusID);

            if (shouldBeActive && !isCurrentlyActive)
            {
                if (busData.Durability > threshold)
                {
                    SpawnBus(busData);
                }
            }
            else if (!shouldBeActive && isCurrentlyActive)
            {
                ReturnBusToDepot(busData.BusID);
            }
        }
    }

    private void SpawnBus(BusData data)
    {
        if (busPrefab == null)
        {
            Debug.LogError($"[Depot] No busPrefab assigned for {depotID}");
            return;
        }

        // Validate Route
        Route route = TransportManager.Instance.GetRoute(data.Schedule.RouteID);
        if (route == null || route.StopIDs.Count == 0) return;
        
        BusStop startStop = TransportManager.Instance.GetStop(route.StopIDs[0]);
        if (startStop == null) return;

        // Instantiate
        GameObject newBusObj = Instantiate(busPrefab, startStop.transform.position, startStop.transform.rotation);
        
        // Setup Network
        var netObj = newBusObj.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
        else
        {
            Destroy(newBusObj);
            return;
        }

        // Initialize Driver
        BusDriver driver = newBusObj.GetComponent<BusDriver>();
        if (driver != null)
        {
            // Note: BusDriver needs to handle the new BusData class type
            driver.ServerInitialize(data, this);
        }

        // Register with Manager
        FleetManager.Instance.RegisterSpawnedBus(data.BusID, newBusObj);
        Debug.Log($"[Depot] Spawned Bus {data.BusID} on Route {route.RouteName}");
    }

    public void ReturnBusToDepot(string busID)
    {
        GameObject activeBus = FleetManager.Instance.GetActiveBus(busID);
        if (activeBus != null)
        {
            var netObj = activeBus.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn();
            }
            else
            {
                Destroy(activeBus);
            }
            
            FleetManager.Instance.UnregisterBus(busID);
            Debug.Log($"[Depot] Bus {busID} returned to depot.");
        }
    }
}