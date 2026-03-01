using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System;
using System.Linq;

[DefaultExecutionOrder(-10)]
public class NetworkSyncBroker : NetworkBehaviour
{
    public static NetworkSyncBroker Instance { get; private set; }

    [Header("Rate Limits (Seconds)")]
    public float companyStatsRate = 0.5f;
    public float fleetStatsRate = 2.0f;
    public float maintenanceStatsRate = 1.0f;
    public float companyLedgerRate = 1.0f;

    private Dictionary<SyncDataType, bool> _dirtyFlags = new Dictionary<SyncDataType, bool>();
    private Dictionary<SyncDataType, float> _timers = new Dictionary<SyncDataType, float>();
    
    // Tracks which Client IDs want which data
    private Dictionary<SyncDataType, HashSet<ulong>> _subscribers = new Dictionary<SyncDataType, HashSet<ulong>>();

    // Events fired when the timer elapses. Managers listen to these to serialize their data.
    public event Action<BaseRpcTarget> OnCompanySyncTriggered;
    public event Action<BaseRpcTarget> OnCompanyLedgerSyncTriggered;
    public event Action<BaseRpcTarget> OnFleetSyncTriggered;
    public event Action<BaseRpcTarget> OnMaintenanceSyncTriggered;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        
        foreach (SyncDataType type in Enum.GetValues(typeof(SyncDataType)))
        {
            if (type == SyncDataType.None) continue;
            _timers[type] = 0f;
            _dirtyFlags[type] = false;
            _subscribers[type] = new HashSet<ulong>();
        }
    }

    public override void OnNetworkSpawn()
    {
        // When the network connects, tell the UI to push its pending subscriptions
        if (!IsServer && LocalDataBroker.Instance != null)
        {
            LocalDataBroker.Instance.ResendSubscriptions();
        }
    }

    public void MarkDirty(SyncDataType type)
    {
        if (!IsServer) return;
        _dirtyFlags[type] = true;
    }

    private void Update()
    {
        if (!IsServer) return;

        ProcessSync(SyncDataType.CompanyStats, companyStatsRate, OnCompanySyncTriggered);
        ProcessSync(SyncDataType.CompanyLedger, companyLedgerRate, OnCompanyLedgerSyncTriggered);
        ProcessSync(SyncDataType.FleetStats, fleetStatsRate, OnFleetSyncTriggered);
        ProcessSync(SyncDataType.MaintenanceStats, maintenanceStatsRate, OnMaintenanceSyncTriggered);
    }

    private void ProcessSync(SyncDataType type, float rateLimit, Action<BaseRpcTarget> syncAction)
    {
        _timers[type] += Time.deltaTime;
        
        if (_dirtyFlags[type])
        {
            if (_timers[type] >= rateLimit)
            {
                var targetClients = _subscribers[type].ToArray();
                if (targetClients.Length > 0)
                {
                    Debug.Log($"<color=green>[Server Throttler]</color> Rate limit hit for {type}. Dispatching to {targetClients.Length} clients.");
                    BaseRpcTarget target = RpcTarget.Group(targetClients, RpcTargetUse.Temp);
                    syncAction?.Invoke(target);
                }
                
                _dirtyFlags[type] = false;
                _timers[type] = 0f;
            }
        }
    }

    // --- Client Opt-in / Opt-out ---

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SubscribeRpc(SyncDataType type, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        _subscribers[type].Add(clientId);
        Debug.Log($"<color=green>[Server Throttler]</color> Received Subscribe request for {type} from Client {clientId}.");

        BaseRpcTarget target = RpcTarget.Single(clientId, RpcTargetUse.Temp);
        
        if (type == SyncDataType.CompanyStats) OnCompanySyncTriggered?.Invoke(target);
        if (type == SyncDataType.CompanyLedger) OnCompanyLedgerSyncTriggered?.Invoke(target);
        if (type == SyncDataType.FleetStats) OnFleetSyncTriggered?.Invoke(target);
        if (type == SyncDataType.MaintenanceStats) OnMaintenanceSyncTriggered?.Invoke(target);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UnsubscribeRpc(SyncDataType type, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        _subscribers[type].Remove(clientId);
    }
}