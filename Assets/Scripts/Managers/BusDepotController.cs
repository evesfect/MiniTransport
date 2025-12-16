using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

// BusDepotController logic is dependant on BusDriver, be cautious!
public class DepotController : NetworkBehaviour
{
    [Header("Identity")]
    public string depotID = "Depot_Main";

    [Header("Fleet Configuration")]
    public GameObject busPrefab; // must have networkobject component
    private List<DepotBusEntry> _activeFleetCache = new List<DepotBusEntry>();


    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }
        if (FleetManager.Instance != null)
        {
            _activeFleetCache = FleetManager.Instance.GetBusesForDepot(depotID);
        }
        if (SimulationTimeManager.Instance != null)
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

    public void CheckSchedules()
    {
        if (_activeFleetCache == null) return;
        float currentTime = SimulationTimeManager.Instance.CurrentTimeOfDay;

        foreach (var entry in _activeFleetCache)
        {
            // Spawning Conditions
            bool shouldBeActive = currentTime >= entry.Schedule.StartTime && currentTime < entry.Schedule.EndTime;
            if (shouldBeActive && entry.CurrentState == BusState.InDepot)
            {
                SpawnBus(entry);
            }
        }
    }

    private void SpawnBus(DepotBusEntry entry)
    {
        if (busPrefab == null) {
            Debug.LogError("DepotBusEntry has no busPrefab!");
            return;
        }
        Route route = TransportManager.Instance.GetRoute(entry.Schedule.RouteID);
        if (route == null || route.StopIDs.Count == 0) return;
        BusStop startStop = TransportManager.Instance.GetStop(route.StopIDs[0]);
        if (startStop == null) return;

        GameObject newBusObj = Instantiate(busPrefab, startStop.transform.position, startStop.transform.rotation);
        var netObj = newBusObj.GetComponent<NetworkObject>();
        if(netObj != null)
        {
            netObj.Spawn(); // replicate to all clients
        }
        else
        {
            Debug.LogError("Bus prefab misses NetworkObject component.");
            Destroy(newBusObj);
            return;
        }

        BusDriver driver = newBusObj.GetComponent<BusDriver>();
        if(driver != null)
        {
            driver.ServerInitialize(entry, this);
        }

        entry.ActiveBusInstance = newBusObj;
        entry.CurrentState = BusState.OnRoute;

        Debug.Log($"[Depot] Spawned Bus {entry.BusID} on Route {route.RouteName}");
    }

    public void ReturnBusToDepot(DepotBusEntry entry)
    {
        if (entry.ActiveBusInstance != null)
        {
            var netObj = entry.ActiveBusInstance.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn();
            }
            else
            {
                Destroy(entry.ActiveBusInstance);
            }
        }

        entry.ActiveBusInstance = null;
        entry.CurrentState = BusState.InDepot;
        Debug.Log($"[Depot] Bus {entry.BusID} returned to depot.");
    }
}