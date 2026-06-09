using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Netcode;

public class LocalDataBroker : MonoBehaviour, ILocalDataProvider
{
    public static LocalDataBroker Instance { get; private set; }

    [SerializeField] private ClientDataCache dataCache;
    private Dictionary<SyncDataType, int> interestCounts = new Dictionary<SyncDataType, int>();

    // Tracks successful local hooks to prevent double-subscribing
    private Dictionary<SyncDataType, bool> _activelyProviding = new Dictionary<SyncDataType, bool>();

    // All report types share KPIManager's single OnReportsUpdated event, so we hook it once.
    private static readonly SyncDataType[] ReportTypes = NetworkSyncBroker.ReportTypes;
    private bool _reportsHooked = false;

    private static bool IsReportType(SyncDataType type) => System.Array.IndexOf(ReportTypes, type) >= 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        // Clean, lightweight retry for local manager hooks only
        foreach (var kvp in interestCounts)
        {
            if (kvp.Value > 0 && !_activelyProviding.GetValueOrDefault(kvp.Key, false))
            {
                TryStartProvidingData(kvp.Key);
            }
        }
    }

    public void RegisterInterest(SyncDataType mask)
    {
        if ((mask & SyncDataType.CompanyStats) != 0) HandleInterestAdded(SyncDataType.CompanyStats);
        if ((mask & SyncDataType.FleetStats) != 0) HandleInterestAdded(SyncDataType.FleetStats);
        if ((mask & SyncDataType.MaintenanceStats) != 0) HandleInterestAdded(SyncDataType.MaintenanceStats);

        foreach (var t in ReportTypes)
            if ((mask & t) != 0) HandleInterestAdded(t);
    }

    public void UnregisterInterest(SyncDataType mask)
    {
        if ((mask & SyncDataType.CompanyStats) != 0) HandleInterestRemoved(SyncDataType.CompanyStats);
        if ((mask & SyncDataType.FleetStats) != 0) HandleInterestRemoved(SyncDataType.FleetStats);
        if ((mask & SyncDataType.MaintenanceStats) != 0) HandleInterestRemoved(SyncDataType.MaintenanceStats);

        foreach (var t in ReportTypes)
            if ((mask & t) != 0) HandleInterestRemoved(t);
    }

    public void ResendSubscriptions()
    {
        // Called cleanly by the Server Throttler the moment the Client officially connects
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer && NetworkSyncBroker.Instance != null)
        {
            foreach (var kvp in interestCounts)
            {
                if (kvp.Value > 0)
                {
                    NetworkSyncBroker.Instance.SubscribeRpc(kvp.Key);
                }
            }
        }
    }

    private void HandleInterestAdded(SyncDataType type)
    {
        if (!interestCounts.ContainsKey(type)) interestCounts[type] = 0;
        interestCounts[type]++;

        if (interestCounts[type] == 1)
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer && NetworkSyncBroker.Instance != null)
            {
                NetworkSyncBroker.Instance.SubscribeRpc(type);
            }
            TryStartProvidingData(type);
        }
        else
        {
            PushCurrentState(type);
        }
    }

    private void HandleInterestRemoved(SyncDataType type)
    {
        if (interestCounts.ContainsKey(type))
        {
            interestCounts[type]--;
            if (interestCounts[type] <= 0)
            {
                interestCounts[type] = 0;
                
                if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer && NetworkSyncBroker.Instance != null)
                {
                    NetworkSyncBroker.Instance.UnsubscribeRpc(type);
                }
                StopProvidingData(type);
            }
        }
    }

    private void TryStartProvidingData(SyncDataType type)
    {
        bool success = false;

        if (type == SyncDataType.CompanyStats && CompanyManager.Instance != null)
        {
            CompanyManager.Instance.OnBalanceChanged -= OnCompanyBalanceUpdated;
            CompanyManager.Instance.OnBalanceChanged += OnCompanyBalanceUpdated;
            PushCurrentState(SyncDataType.CompanyStats);
            success = true;
        }
        else if (type == SyncDataType.FleetStats && FleetManager.Instance != null)
        {
            FleetManager.Instance.OnFleetUpdated -= OnFleetUpdated;
            FleetManager.Instance.OnFleetUpdated += OnFleetUpdated;
            PushCurrentState(SyncDataType.FleetStats);
            success = true;
        }
        else if (type == SyncDataType.MaintenanceStats && FleetManager.Instance != null && MaintenanceManager.Instance != null)
        {
            FleetManager.Instance.OnFleetUpdated -= OnMaintenanceUpdated;
            FleetManager.Instance.OnFleetUpdated += OnMaintenanceUpdated;
            PushCurrentState(SyncDataType.MaintenanceStats);
            success = true;
        }
        else if (IsReportType(type) && KPIManager.Instance != null)
        {
            if (!_reportsHooked)
            {
                KPIManager.Instance.OnReportsUpdated += OnKpiReportsUpdated;
                _reportsHooked = true;
            }
            PushCurrentState(type);
            success = true;
        }

        if (success) _activelyProviding[type] = true;
    }

    private void StopProvidingData(SyncDataType type)
    {
        _activelyProviding[type] = false;
        
        if (type == SyncDataType.CompanyStats && CompanyManager.Instance != null)
            CompanyManager.Instance.OnBalanceChanged -= OnCompanyBalanceUpdated;
        else if (type == SyncDataType.FleetStats && FleetManager.Instance != null)
            FleetManager.Instance.OnFleetUpdated -= OnFleetUpdated;
        else if (type == SyncDataType.MaintenanceStats && FleetManager.Instance != null)
            FleetManager.Instance.OnFleetUpdated -= OnMaintenanceUpdated;
        else if (IsReportType(type) && _reportsHooked && !AnyReportInterest() && KPIManager.Instance != null)
        {
            KPIManager.Instance.OnReportsUpdated -= OnKpiReportsUpdated;
            _reportsHooked = false;
        }
    }

    private void OnCompanyBalanceUpdated(float amt) => PushCurrentState(SyncDataType.CompanyStats);
    private void OnFleetUpdated() => PushCurrentState(SyncDataType.FleetStats);
    private void OnMaintenanceUpdated() => PushCurrentState(SyncDataType.MaintenanceStats);

    private bool AnyReportInterest()
    {
        foreach (var t in ReportTypes)
            if (interestCounts.GetValueOrDefault(t, 0) > 0) return true;
        return false;
    }

    // KPIManager raised a fresh snapshot — re-push every report the UI currently cares about.
    private void OnKpiReportsUpdated()
    {
        foreach (var t in ReportTypes)
            if (interestCounts.GetValueOrDefault(t, 0) > 0)
                PushCurrentState(t);
    }

    private void PushCurrentState(SyncDataType type)
    {
        if (type == SyncDataType.CompanyStats && CompanyManager.Instance != null)
        {
            CompanyData realData = CompanyManager.Instance.GetCompanyData();
            if (realData != null)
                dataCache.SetCompanyData(new CompanyStatsData { currentBalance = realData.CurrentBalance });
        }
        else if (type == SyncDataType.FleetStats && FleetManager.Instance != null)
        {
            var currentBuses = FleetManager.Instance.allBuses;
            dataCache.SetFleetData(new FleetStatsData {
                totalBuses = currentBuses.Count,
                lowDurabilityBuses = currentBuses.Count(b => b.GetAverageHealth() < 50f)
            });
        }
        else if (type == SyncDataType.MaintenanceStats && MaintenanceManager.Instance != null && FleetManager.Instance != null)
        {
            List<BusHealthData> healthList = FleetManager.Instance.allBuses.Select(b => new BusHealthData
            {
                busID = b.BusID,durability = b.GetAverageHealth()
            }).ToList();

            dataCache.SetMaintenanceData(new MaintenanceStatsData {
                operationalThreshold = MaintenanceManager.Instance.operationalThreshold,
                breakdownThreshold = MaintenanceManager.Instance.breakdownThreshold,
                busHealthList = healthList
            });
        }
        else if (IsReportType(type) && KPIManager.Instance != null)
        {
            switch (type)
            {
                case SyncDataType.GeneralReport: dataCache.SetGeneralReport(KPIManager.Instance.GetGeneralReport()); break;
                case SyncDataType.OperationsReport: dataCache.SetOperationsReport(KPIManager.Instance.GetOperationsReport()); break;
                case SyncDataType.MaintenanceReport: dataCache.SetMaintenanceReport(KPIManager.Instance.GetMaintenanceReport()); break;
                case SyncDataType.HrReport: dataCache.SetHrReport(KPIManager.Instance.GetHrReport()); break;
                case SyncDataType.FinanceReport: dataCache.SetFinanceReport(KPIManager.Instance.GetFinanceReport()); break;
                case SyncDataType.VendorReport: dataCache.SetVendorReport(KPIManager.Instance.GetVendorReport()); break;
            }
        }
    }
}