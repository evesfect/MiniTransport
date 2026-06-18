using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static UnityEditor.Experimental.GraphView.Port;

[DefaultExecutionOrder(-50)]
public class FleetManager : NetworkBehaviour
{
    public static FleetManager Instance { get; private set; }

    [Header("Master Fleet Data")]
    public List<BusData> allBuses = new List<BusData>();

    [Header("Financial Settings")]
    public float weeklyCostPerBus = 150f;

    public event Action OnFleetUpdated;

    // Runtime Lookup: Maps BusID -> Spawned GameObject (server-only)
    private Dictionary<string, GameObject> _activeBusInstances = new Dictionary<string, GameObject>();

    // Mirrors the number of spawned/in-service buses to every client. Written by the server
    // whenever a bus is registered/unregistered, since _activeBusInstances only exists on the server.
    private readonly NetworkVariable<int> _netActiveBusCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Per-bus live status (active/returning/broken) mirrored to clients. The server keeps the
    // authoritative spawned-instance map (_activeBusInstances); this list is its client-visible
    // shadow so fleet UI on any peer can show the real driving status.
    private readonly NetworkList<BusRuntimeStatus> _activeStatuses = new NetworkList<BusRuntimeStatus>();

    public enum FleetOperation { Add, Remove, Update }

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
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            LoadFleet();

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

            if (CompanyManager.Instance != null)
            {
                CompanyManager.Instance.OnWeeklyExpensesRequested += SubmitFleetExpenses;
            }

            // Hook into the throttler
            if (NetworkSyncBroker.Instance != null)
            {
                NetworkSyncBroker.Instance.OnFleetSyncTriggered += PerformFleetSync;
            }

            OnFleetUpdated?.Invoke();
        }
        else
        {
            allBuses.Clear();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {

            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

            if (CompanyManager.Instance != null)
                CompanyManager.Instance.OnWeeklyExpensesRequested -= SubmitFleetExpenses;

            // Unhook from the throttler
            if (NetworkSyncBroker.Instance != null)
            {
                NetworkSyncBroker.Instance.OnFleetSyncTriggered -= PerformFleetSync;
            }
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer) SyncFleetRpc(SerializeFleet(), RpcTarget.Single(clientId, RpcTargetUse.Temp));
    }

    private void SubmitFleetExpenses()
    {
        if (allBuses.Count == 0) return;

        float totalTax = allBuses.Count * weeklyCostPerBus;

        Debug.Log($"[FleetManager] Submitting weekly tax: {totalTax}");

        CompanyManager.Instance.ProcessPassiveExpense(
            totalTax,
            TransactionCategory.Tax,
            $"Weekly Fleet Tax ({allBuses.Count} buses)"
        );
    }

    private void OnApplicationQuit()
    {
        if (IsServer) SaveFleet();
    }

    // --- Runtime Management (Server Only) ---

    /// <summary>True if the bus is currently spawned/driving. Works on clients via the mirrored
    /// status list; the server reads its authoritative instance map.</summary>
    public bool IsBusActive(string busID)
    {
        if (IsServer)
            return _activeBusInstances.ContainsKey(busID) && _activeBusInstances[busID] != null;

        return TryGetStatus(busID, out _);
    }

    /// <summary>True if the bus is out driving and has been recalled (returning to its depot).</summary>
    public bool IsBusReturning(string busID) => TryGetStatus(busID, out var s) && s.IsRecalled;

    /// <summary>True if the bus is out driving and currently broken down on the road.</summary>
    public bool IsBusBroken(string busID) => TryGetStatus(busID, out var s) && s.IsBroken;

    private bool TryGetStatus(string busID, out BusRuntimeStatus status)
    {
        for (int i = 0; i < _activeStatuses.Count; i++)
        {
            if (_activeStatuses[i].BusID.Equals(busID)) { status = _activeStatuses[i]; return true; }
        }
        status = default;
        return false;
    }

    public void RegisterSpawnedBus(string busID, GameObject busInstance)
    {
        if (_activeBusInstances.ContainsKey(busID))
            _activeBusInstances[busID] = busInstance;
        else
            _activeBusInstances.Add(busID, busInstance);

        SetStatusEntry(busID, recalled: false, broken: false);
        PublishActiveBusCount();
    }

    public void UnregisterBus(string busID)
    {
        if (_activeBusInstances.ContainsKey(busID))
            _activeBusInstances.Remove(busID);

        RemoveStatusEntry(busID);
        PublishActiveBusCount();
    }

    // --- Synced runtime status (server-only writers) ---

    /// <summary>Server: flag/unflag a live bus as recalled (returning to depot) for client UI.</summary>
    public void SetBusRecalled(string busID, bool recalled) => UpdateStatusFlag(busID, recalled: recalled);

    /// <summary>Server: flag/unflag a live bus as broken down for client UI.</summary>
    public void SetBusBroken(string busID, bool broken) => UpdateStatusFlag(busID, broken: broken);

    private void UpdateStatusFlag(string busID, bool? recalled = null, bool? broken = null)
    {
        if (!IsServer) return;
        for (int i = 0; i < _activeStatuses.Count; i++)
        {
            if (!_activeStatuses[i].BusID.Equals(busID)) continue;
            var s = _activeStatuses[i];
            if (recalled.HasValue) s.IsRecalled = recalled.Value;
            if (broken.HasValue) s.IsBroken = broken.Value;
            _activeStatuses[i] = s;
            return;
        }
    }

    private void SetStatusEntry(string busID, bool recalled, bool broken)
    {
        if (!IsServer) return;
        var entry = new BusRuntimeStatus { BusID = busID, IsRecalled = recalled, IsBroken = broken };
        for (int i = 0; i < _activeStatuses.Count; i++)
        {
            if (_activeStatuses[i].BusID.Equals(busID)) { _activeStatuses[i] = entry; return; }
        }
        _activeStatuses.Add(entry);
    }

    private void RemoveStatusEntry(string busID)
    {
        if (!IsServer) return;
        for (int i = 0; i < _activeStatuses.Count; i++)
        {
            if (_activeStatuses[i].BusID.Equals(busID)) { _activeStatuses.RemoveAt(i); return; }
        }
    }

    // Server-only: recompute the live count and mirror it to clients.
    private void PublishActiveBusCount()
    {
        if (!IsServer) return;
        _netActiveBusCount.Value = CountActiveInstances();
    }

    private int CountActiveInstances()
    {
        int count = 0;
        foreach (var instance in _activeBusInstances.Values)
            if (instance != null) count++;
        return count;
    }

    public GameObject GetActiveBus(string busID)
    {
        if (_activeBusInstances.TryGetValue(busID, out GameObject obj)) return obj;
        return null;
    }

    /// <summary>
    /// Number of buses currently spawned/in-service. Works on clients too: the server keeps
    /// the authoritative dictionary, while clients read the mirrored network value.
    /// </summary>
    public int ActiveBusCount => IsServer ? CountActiveInstances() : _netActiveBusCount.Value;

    // --- Networking & Data ---

    private void PerformFleetSync(BaseRpcTarget target)
    {
        string json = SerializeFleet();
        SyncFleetRpc(json, target);
    }

    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
    private void SyncFleetRpc(string jsonFleet, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        
        FleetContainer container = JsonUtility.FromJson<FleetContainer>(jsonFleet);
        if (container != null && container.Buses != null)
        {
            allBuses = container.Buses;
            foreach (var bus in allBuses)
            {
                if (bus.Parts == null || bus.Parts.Count == 0)
                {
                    bus.InitializeParts();
                }
            }
            Debug.Log($"[FleetManager] Synced {allBuses.Count} buses from Server.");
            OnFleetUpdated?.Invoke();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestFleetOperationRpc(string busEntryJson, FleetOperation op)
    {
        BusData requestEntry = JsonUtility.FromJson<BusData>(busEntryJson);
        if (requestEntry == null) return;

        bool changed = false;

        switch (op)
        {
            case FleetOperation.Add:
                if (!allBuses.Any(b => b.BusID == requestEntry.BusID))
                {
                    requestEntry.InitializeParts();
                    allBuses.Add(requestEntry);
                    changed = true;
                }
                break;

            case FleetOperation.Update:
                int idx = allBuses.FindIndex(b => b.BusID == requestEntry.BusID);
                if (idx != -1)
                {
                    allBuses[idx].AssignedDepotID = requestEntry.AssignedDepotID;
                    allBuses[idx].Schedule = requestEntry.Schedule;
                    changed = true;
                }
                break;

            case FleetOperation.Remove:
                BusData toRemove = allBuses.FirstOrDefault(b => b.BusID == requestEntry.BusID);
                if (toRemove != null)
                {
                    allBuses.Remove(toRemove);
                    changed = true;
                }
                break;
        }

        if (changed)
        {
            SaveFleet();
            OnFleetUpdated?.Invoke();

            // Tell the Rate Limiter we have new data instead of blasting the RPC
            if (NetworkSyncBroker.Instance != null)
            {
                NetworkSyncBroker.Instance.MarkDirty(SyncDataType.FleetStats);
                NetworkSyncBroker.Instance.MarkDirty(SyncDataType.MaintenanceStats);
            }
        }
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
                    foreach (var bus in allBuses)
                    {
                        if (bus.Parts == null || bus.Parts.Count == 0)
                        {
                            bus.InitializeParts();
                        }
                    }
                    // Clear runtime maps on load to prevent stale references
                    _activeBusInstances.Clear();
                    if (IsSpawned && IsServer) _activeStatuses.Clear();
                    Debug.Log($"FleetManager: Loaded {allBuses.Count} buses.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"FleetManager: Failed to load fleet.json. Error: {e.Message}");
            }
        }
    }

    // Data Modificiation API
    public void UpdateBusPartHealth(string busID, BusPartType partType, float newHealth)
    {
        var bus = allBuses.FirstOrDefault(b => b.BusID == busID);
        if (bus != null && bus.Parts != null)
        {
            var part = bus.Parts.FirstOrDefault(p => p.PartType == partType);
            if (part != null)
            {
                part.Health = Mathf.Clamp(newHealth, 0f, 100f);
            }

            OnFleetUpdated?.Invoke();

            if (NetworkSyncBroker.Instance != null)
            {
                NetworkSyncBroker.Instance.MarkDirty(SyncDataType.FleetStats);
                NetworkSyncBroker.Instance.MarkDirty(SyncDataType.MaintenanceStats);
            }
        }
    }

    public void UpdateBusPartMaxLife(string busID, BusPartType partType, float newMaxLife)
    {
        var bus = allBuses.FirstOrDefault(b => b.BusID == busID);
        if (bus != null && bus.Parts != null)
        {
            var part = bus.Parts.FirstOrDefault(p => p.PartType == partType);
            if (part != null)
            {
                part.MaxLife = Mathf.Clamp(newMaxLife, 10f, 100f); // Keep min 10% structural integrity
                // Health cannot exceed MaxLife
                if (part.Health > part.MaxLife) part.Health = part.MaxLife;
            }
            OnFleetUpdated?.Invoke();

            // Tell the Rate Limiter we have new data
            if (NetworkSyncBroker.Instance != null)
            {
                NetworkSyncBroker.Instance.MarkDirty(SyncDataType.FleetStats);
                NetworkSyncBroker.Instance.MarkDirty(SyncDataType.MaintenanceStats);
            }
        }
    }

    // Client Helper Wrappers
    public List<BusData> GetBusesForDepot(string depotID)
    {
        return allBuses.Where(b => b.AssignedDepotID == depotID).ToList();
    }

    public void CreateBusClient(string busID, string depotID, BusSchedule schedule, ushort capacity)
    {
        BusData newBus = new BusData
        {
            BusID = busID,
            AssignedDepotID = depotID,
            Schedule = schedule,
            Capacity = capacity
        };
        RequestFleetOperationRpc(JsonUtility.ToJson(newBus), FleetOperation.Add);
    }

    public void DeleteBusClient(string busID)
    {
        RequestFleetOperationRpc(JsonUtility.ToJson(new BusData { BusID = busID }), FleetOperation.Remove);
    }

    public void UpdateBusClient(BusData entry)
    {
        RequestFleetOperationRpc(JsonUtility.ToJson(entry), FleetOperation.Update);
    }

    // --- Recall ---

    /// <summary>
    /// Client → server: recall an in-service bus back to its depot. Resolved on the server, which
    /// owns the live bus instances. No-op if the bus is not currently spawned (already parked).
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestRecallBusRpc(string busID)
    {
        GameObject busObj = GetActiveBus(busID);
        if (busObj == null) return;

        if (busObj.TryGetComponent<BusDriver>(out var driver))
            driver.RecallToDepot();
    }

    /// <summary>Client-facing wrapper to recall a bus to its depot.</summary>
    public void RecallBusClient(string busID)
    {
        if (string.IsNullOrEmpty(busID)) return;
        RequestRecallBusRpc(busID);
    }
}

[System.Serializable]
public class FleetContainer
{
    public List<BusData> Buses;
}