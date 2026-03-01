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

    private RecoveryVehicle _activeRecoveryVehicle;

    public bool IsRecoveryAvailable
    {
        get
        {
            // If we haven't spawned one, we are available. 
            // If we have, check if it's busy.
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

        // 1. Spawn if doesn't exist
        if (_activeRecoveryVehicle == null)
        {
            GameObject go = Instantiate(recoveryVehiclePrefab.gameObject, SpawnNode.transform.position, SpawnNode.transform.rotation);
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj != null) netObj.Spawn();
            _activeRecoveryVehicle = go.GetComponent<RecoveryVehicle>();
        }

        // 2. Start Mission
        _activeRecoveryVehicle.StartMission(busID, this);
    }

    // Called by RecoveryVehicle when it returns
    public void OnRecoveryVehicleFinished()
    {
        // Tell the Manager we are ready for the next job
        if (MaintenanceManager.Instance != null)
        {
            MaintenanceManager.Instance.OnDepotFree(this.depotID);
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
                    if (EmployeeManager.Instance != null && EmployeeManager.Instance.HasAssignedDriver(busData.BusID))
                    {
                        SpawnBus(busData);
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
                    // If driver is missing or Broken, DO NOT despawn
                    if (driver != null && driver.IsBroken)
                    {
                        canReturn = false;
                        // Debug.Log($"[Depot] Keeping Bus {busData.BusID} active despite schedule end because it is BROKEN.");
                    }
                }

                if (canReturn)
                {
                    ReturnBusToDepot(busData.BusID);
                }
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