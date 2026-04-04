using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class DepotController : NetworkBehaviour
{
    [Header("Identity")]
    public string depotID = "Depot_Main";

    [Header("Fleet Configuration")]
    public GameObject busPrefab;

    [Header("Recovery Configuration")]
    public GameObject recoveryVehiclePrefab;
  
    [Header("Recovery Spawn Point")]
    public RoadNode SpawnNode;

    [Header("Debug")]
    [Tooltip("If true, ignores wear and tear, allowing all buses to spawn.")]
    public bool disableMaintenanceChecks = false;

    private RecoveryVehicle _activeRecoveryVehicle;

    public bool IsRecoveryAvailable
    {
        get
        {
            return _activeRecoveryVehicle == null || !_activeRecoveryVehicle.IsBusy;
        }
    }

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

    public void DispatchRecoveryVehicle(string busID)
    {
        if (!IsServer) return;
        if (SpawnNode == null)
        {
            Debug.LogError($"[Depot {depotID}] Cannot dispatch recovery: No SpawnNode assigned!");
            return;
        }

        if (_activeRecoveryVehicle == null)
        {
            GameObject go = Instantiate(recoveryVehiclePrefab.gameObject, SpawnNode.transform.position, SpawnNode.transform.rotation);
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj != null) netObj.Spawn();
            _activeRecoveryVehicle = go.GetComponent<RecoveryVehicle>();
        }

        _activeRecoveryVehicle.StartMission(busID, this);
    }

    public void OnRecoveryVehicleFinished()
    {
        if (MaintenanceManager.Instance != null)
        {
            MaintenanceManager.Instance.OnDepotFree(this.depotID);
        }
    }

    public void CheckSchedules()
    {
        if (FleetManager.Instance == null) return;
        float threshold = MaintenanceManager.Instance != null ? MaintenanceManager.Instance.operationalThreshold : 15f;

        float currentTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        var myBuses = FleetManager.Instance.allBuses.Where(b => b.AssignedDepotID == depotID);

        foreach (var busData in myBuses)
        {
            bool shouldBeActive = currentTime >= busData.Schedule.StartTime && currentTime < busData.Schedule.EndTime;
            bool isCurrentlyActive = FleetManager.Instance.IsBusActive(busData.BusID);

            if (shouldBeActive && !isCurrentlyActive)
            {
                if (IsBusConditionGoodEnough(busData, threshold))
                {
                    if (EmployeeManager.Instance != null && EmployeeManager.Instance.HasAssignedDriver(busData.BusID))
                    {
                        SpawnBus(busData);
                    }
                    else
                    {
                        Debug.LogWarning($"[Depot] Bus {busData.BusID} cannot spawn: No assigned driver.");
                    }
                }
            }
            else if (!shouldBeActive && isCurrentlyActive)
            {
                bool canReturn = true;

                GameObject busObj = FleetManager.Instance.GetActiveBus(busData.BusID);
                if (busObj != null)
                {
                    BusDriver driver = busObj.GetComponent<BusDriver>();
                    if (driver != null && driver.IsBroken )
                    {
                        canReturn = false;
                    }

                    if(driver != null && driver.PassengerCount > 0) 
                    {
                        canReturn = false;
                    }
                }

                if (canReturn)
                {
                    ReturnBusToDepot(busData.BusID);
                }
            }
        }
    }

    private bool IsBusConditionGoodEnough(BusData data, float avgThreshold)
    {
        if (disableMaintenanceChecks) return true;

        if (data.Parts == null || data.Parts.Count == 0) return true;

        // Loop through to provide granular debugging on the exact failure point
        foreach (var p in data.Parts)
        {
            if (p.Health <= 0f)
            {
                Debug.LogWarning($"[Depot] Bus {data.BusID} failed check: Part {p.PartType} Health is {p.Health} (Critical 0% failure).");
                return false;
            }

            if (p.MaxLife < avgThreshold)
            {
                Debug.LogWarning($"[Depot] Bus {data.BusID} failed check: Part {p.PartType} MaxLife is {p.MaxLife} (Permanently degraded below operational threshold of {avgThreshold}).");
                return false;
            }
        }

        float sum = 0f;
        foreach (var p in data.Parts) sum += p.Health;
        float avg = sum / data.Parts.Count;

        if (avg <= avgThreshold)
        {
            Debug.LogWarning($"[Depot] Bus {data.BusID} failed check: Average health is {avg:F1} (Below threshold {avgThreshold}).");
            return false;
        }

        return true;
    }

    private void SpawnBus(BusData data)
    {
        if (busPrefab == null)
        {
            Debug.LogError($"[Depot] No busPrefab assigned for {depotID}");
            return;
        }

        Route route = TransportManager.Instance.GetRoute(data.Schedule.RouteID);
        if (route == null || route.StopIDs.Count == 0) return;
        
        BusStop startStop = TransportManager.Instance.GetStop(route.StopIDs[0]);
        if (startStop == null) return;

        GameObject newBusObj = Instantiate(busPrefab, startStop.transform.position, startStop.transform.rotation);
        
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

        BusDriver driver = newBusObj.GetComponent<BusDriver>();
        if (driver != null)
        {
            driver.ServerInitialize(data, this);
        }

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