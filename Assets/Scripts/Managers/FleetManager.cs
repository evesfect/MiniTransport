using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using System.ComponentModel;

[DefaultExecutionOrder(-50)] // Init after TimeManager, but before DepotController
public class FleetManager : NetworkBehaviour
{
    public static FleetManager Instance { get; private set; }

    [Header("Master Fleet Data")]
    public List<DepotBusEntry> allBuses = new List<DepotBusEntry>();
    public enum FleetOperation { Add, Remove, Update }

    // persistentDataPath for builds, dataPath for Editor visibility
#if UNITY_EDITOR
    private string SavePath => Path.Combine(Application.dataPath, "fleet.json");
#else
    private string SavePath => Path.Combine(Application.persistentDataPath, "fleet.json");
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        LoadFleet();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            LoadFleet();
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
        else
        {
            allBuses.Clear(); // client starts empty until synced
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            string json = SerializeFleet();
            SyncFleetRpc(json, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }

    private void OnApplicationQuit()
    {
        if (IsServer) SaveFleet();
    }

    // RPCs

    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
    private void SyncFleetRpc(string jsonFleet, RpcParams rpcParams = default)
    {
        FleetContainer container = JsonUtility.FromJson<FleetContainer>(jsonFleet);
        if (container != null && container.Buses != null)
        {
            allBuses = container.Buses;
            foreach(var bus in allBuses)
            {
                bus.ActiveBusInstance = null;
                bus.CurrentState = BusState.InDepot;
            }
            Debug.Log($"[FleetManager] Synced {allBuses.Count} buses from Server.");
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestFleetOperationRpc(string busEntryJson, FleetOperation op)
    {
        DepotBusEntry requestEntry = JsonUtility.FromJson<DepotBusEntry>(busEntryJson);
        if (requestEntry == null) return;

        bool changed = false;

        switch (op)
        {
            case FleetOperation.Add:
                // Check for duplicates
                if (!allBuses.Any(b => b.BusID == requestEntry.BusID))
                {
                    // Ensure clean state
                    requestEntry.CurrentState = BusState.InDepot;
                    requestEntry.ActiveBusInstance = null;
                    allBuses.Add(requestEntry);
                    changed = true;
                }
                break;

            case FleetOperation.Update:
                int idx = allBuses.FindIndex(b => b.BusID == requestEntry.BusID);
                if (idx != -1)
                {
                    // Update data (Assignments/Schedule)
                    allBuses[idx].AssignedDepotID = requestEntry.AssignedDepotID;
                    allBuses[idx].Schedule = requestEntry.Schedule;
                    changed = true;
                }
                break;

            case FleetOperation.Remove:
                DepotBusEntry toRemove = allBuses.FirstOrDefault(b => b.BusID == requestEntry.BusID);
                if (toRemove != null)
                {
                    allBuses.Remove(toRemove);
                    changed = true;
                }
                break;
        }

        if (changed)
        {
            SaveFleet(); // Persist changes on Server
            
            // Broadcast new state to ALL clients
            string json = SerializeFleet();
            SyncFleetRpc(json);
            
            Debug.Log($"[FleetManager] Fleet Operation {op} applied for {requestEntry.BusID}");
        }
    }


    // Client API



    /// <summary>
    /// Returns all buses assigned to the specified Depot ID.
    /// </summary>
    public List<DepotBusEntry> GetBusesForDepot(string depotID)
    {
        return allBuses.Where(b => b.AssignedDepotID == depotID).ToList();
    }

    public void CreateBusClient(string busID, string depotID, BusSchedule schedule)
    {
        DepotBusEntry newBus = new DepotBusEntry
        {
            BusID = busID,
            AssignedDepotID = depotID,
            Schedule = schedule,
            CurrentState = BusState.InDepot
        };
        
        string json = JsonUtility.ToJson(newBus);
        RequestFleetOperationRpc(json, FleetOperation.Add);
    }

    public void DeleteBusClient(string busID)
    {
        // Minimal object for ID matching
        DepotBusEntry dummy = new DepotBusEntry { BusID = busID };
        string json = JsonUtility.ToJson(dummy);
        RequestFleetOperationRpc(json, FleetOperation.Remove);
    }

    public void UpdateBusClient(DepotBusEntry entry)
    {
        string json = JsonUtility.ToJson(entry);
        RequestFleetOperationRpc(json, FleetOperation.Update);
    }

    // --- Persistence ---

    private string SerializeFleet()
    {
        FleetContainer container = new FleetContainer { Buses = allBuses };
        return JsonUtility.ToJson(container, true);
    }

    [ContextMenu("Save Fleet")]
    public void SaveFleet()
    {
        string json = SerializeFleet();
        File.WriteAllText(SavePath, json);
        Debug.Log($"FleetManager: Saved {allBuses.Count} buses to {SavePath}");
    }

    [ContextMenu("Load Fleet")]
    public void LoadFleet()
    {
        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);
                FleetContainer container = JsonUtility.FromJson<FleetContainer>(json);
                if (container != null && container.Buses != null)
                {
                    allBuses = container.Buses;
                    
                    // Reset runtime states on load
                    foreach(var bus in allBuses) 
                    {
                        bus.ActiveBusInstance = null;
                        bus.CurrentState = BusState.InDepot; 
                    }
                    Debug.Log($"FleetManager: Loaded {allBuses.Count} buses.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"FleetManager: Failed to load fleet.json. Error: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"FleetManager: No fleet file found at {SavePath}");
        }
    }
}

[System.Serializable]
public class FleetContainer
{
    public List<DepotBusEntry> Buses;
}