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
    public float companyLedgerRate = 1.0f;
    public float fleetStatsRate = 2.0f;
    public float maintenanceStatsRate = 1.0f;
    [Tooltip("Shared rate limit for all end-of-game report snapshots (infrequent).")]
    public float reportRate = 5.0f;

    // Report snapshots all behave identically (slow rate, server builds a small struct,
    // sends to subscribers), so they share one event keyed by type instead of six.
    public static readonly SyncDataType[] ReportTypes =
    {
        SyncDataType.GeneralReport,
        SyncDataType.OperationsReport,
        SyncDataType.MaintenanceReport,
        SyncDataType.HrReport,
        SyncDataType.FinanceReport,
        SyncDataType.VendorReport
    };

    private static bool IsReportType(SyncDataType type)
    {
        return Array.IndexOf(ReportTypes, type) >= 0;
    }

    private Dictionary<SyncDataType, bool> _dirtyFlags = new Dictionary<SyncDataType, bool>();
    private Dictionary<SyncDataType, float> _timers = new Dictionary<SyncDataType, float>();
    
    private Dictionary<SyncDataType, HashSet<ulong>> _subscribers = new Dictionary<SyncDataType, HashSet<ulong>>();

    public event Action<BaseRpcTarget> OnCompanySyncTriggered;
    public event Action<BaseRpcTarget> OnCompanyLedgerSyncTriggered;
    public event Action<BaseRpcTarget> OnFleetSyncTriggered;
    public event Action<BaseRpcTarget> OnMaintenanceSyncTriggered;
    // Fired for any report type; the (type, target) pair tells KPIManager what to build/send.
    public event Action<SyncDataType, BaseRpcTarget> OnReportSyncTriggered;

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

        foreach (var reportType in ReportTypes)
            ProcessReportSync(reportType);
    }

    private void ProcessReportSync(SyncDataType type)
    {
        _timers[type] += Time.deltaTime;

        if (_dirtyFlags[type] && _timers[type] >= reportRate)
        {
            var targetClients = _subscribers[type].ToArray();
            if (targetClients.Length > 0)
            {
                BaseRpcTarget target = RpcTarget.Group(targetClients, RpcTargetUse.Temp);
                OnReportSyncTriggered?.Invoke(type, target);
            }

            _dirtyFlags[type] = false;
            _timers[type] = 0f;
        }
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
                    BaseRpcTarget target = RpcTarget.Group(targetClients, RpcTargetUse.Temp);
                    syncAction?.Invoke(target);
                }
                
                _dirtyFlags[type] = false;
                _timers[type] = 0f;
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SubscribeRpc(SyncDataType type, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        _subscribers[type].Add(clientId);
        
        // Immediately deliver the current state to the new subscriber
        BaseRpcTarget target = RpcTarget.Single(clientId, RpcTargetUse.Temp);
        
        if (type == SyncDataType.CompanyStats) OnCompanySyncTriggered?.Invoke(target);
        if (type == SyncDataType.CompanyLedger) OnCompanyLedgerSyncTriggered?.Invoke(target);
        if (type == SyncDataType.FleetStats) OnFleetSyncTriggered?.Invoke(target);
        if (type == SyncDataType.MaintenanceStats) OnMaintenanceSyncTriggered?.Invoke(target);
        if (IsReportType(type)) OnReportSyncTriggered?.Invoke(type, target);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UnsubscribeRpc(SyncDataType type, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        _subscribers[type].Remove(clientId);
    }
}