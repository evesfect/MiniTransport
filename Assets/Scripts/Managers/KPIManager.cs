using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Central, server-authoritative aggregator for the end-of-game report KPIs.
/// It accumulates lightweight counters by subscribing to events that already fire across
/// the other managers (zero added per-frame network traffic), builds one small flat struct
/// per report on demand, and syncs each only to clients who opened that panel via the
/// existing NetworkSyncBroker subscription model.
///
/// Metric depth (per design): everything derivable today is real; On-Time Performance is an
/// approximation (served vs missed passengers) and Number Of Transfers is a stub until
/// passenger journey identity exists (see PassengerManager TODO).
/// </summary>
[DefaultExecutionOrder(-40)] // After the managers it reads from
public class KPIManager : NetworkBehaviour
{
    public static KPIManager Instance { get; private set; }

    // --- Lifetime counters (server-side; cumulative over the whole session) ---
    private int _totalBreakdowns;
    private int _repairsCompleted;
    private int _partsReplaced;

    private int _passengersServed;
    private float _totalWaitHours;     // summed wait time of served passengers
    private int _passengersTimedOut;

    private float _utilSampleSum;      // running sum of (active/total*100) samples
    private int _utilSampleCount;

    private int _totalHires;

    // --- Client-received snapshots (populated by RPCs on non-server peers) ---
    private GeneralReportData _generalReport;
    private OperationsReportData _operationsReport;
    private MaintenanceReportData _maintenanceReport;
    private HrReportData _hrReport;
    private FinanceReportData _financeReport;
    private VendorReportData _vendorReport;

    /// <summary>Fired whenever any report data changes (host: counters; client: RPC received).</summary>
    public event Action OnReportsUpdated;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (MaintenanceManager.Instance != null)
        {
            MaintenanceManager.Instance.OnBreakdownOccurred += OnBreakdown;
            MaintenanceManager.Instance.OnRepairCompleted += OnRepairCompleted;
            MaintenanceManager.Instance.OnPartReplaced += OnPartReplaced;
        }

        if (PassengerManager.Instance != null)
        {
            PassengerManager.Instance.OnPassengersServed += OnPassengersServed;
            PassengerManager.Instance.OnPassengersTimedOut += OnPassengersTimedOut;
        }

        if (CompanyManager.Instance != null)
            CompanyManager.Instance.OnTransferRecorded += OnTransferRecorded;

        if (EmployeeManager.Instance != null)
            EmployeeManager.Instance.OnEmployeeHired += OnEmployeeHired;

        if (SimulationTimeManager.Instance != null)
        {
            SimulationTimeManager.Instance.OnHourChanged += SampleUtilization;
            SimulationTimeManager.Instance.OnDayChanged += OnDayChanged;
        }

        if (NetworkSyncBroker.Instance != null)
            NetworkSyncBroker.Instance.OnReportSyncTriggered += PerformReportSync;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;

        if (MaintenanceManager.Instance != null)
        {
            MaintenanceManager.Instance.OnBreakdownOccurred -= OnBreakdown;
            MaintenanceManager.Instance.OnRepairCompleted -= OnRepairCompleted;
            MaintenanceManager.Instance.OnPartReplaced -= OnPartReplaced;
        }

        if (PassengerManager.Instance != null)
        {
            PassengerManager.Instance.OnPassengersServed -= OnPassengersServed;
            PassengerManager.Instance.OnPassengersTimedOut -= OnPassengersTimedOut;
        }

        if (CompanyManager.Instance != null)
            CompanyManager.Instance.OnTransferRecorded -= OnTransferRecorded;

        if (EmployeeManager.Instance != null)
            EmployeeManager.Instance.OnEmployeeHired -= OnEmployeeHired;

        if (SimulationTimeManager.Instance != null)
        {
            SimulationTimeManager.Instance.OnHourChanged -= SampleUtilization;
            SimulationTimeManager.Instance.OnDayChanged -= OnDayChanged;
        }

        if (NetworkSyncBroker.Instance != null)
            NetworkSyncBroker.Instance.OnReportSyncTriggered -= PerformReportSync;
    }

    // ============================================================
    // COLLECTION HANDLERS (server only)
    // ============================================================

    private void OnBreakdown(string busID, BusPartType reason)
    {
        _totalBreakdowns++;
        MarkReportsDirty(SyncDataType.GeneralReport, SyncDataType.MaintenanceReport);
    }

    private void OnRepairCompleted()
    {
        _repairsCompleted++;
        MarkReportsDirty(SyncDataType.MaintenanceReport);
    }

    private void OnPartReplaced()
    {
        _partsReplaced++;
        MarkReportsDirty(SyncDataType.MaintenanceReport);
    }

    private void OnPassengersServed(float waitHours, int count)
    {
        _passengersServed += count;
        _totalWaitHours += waitHours * count;
        MarkReportsDirty(SyncDataType.GeneralReport, SyncDataType.OperationsReport);
    }

    private void OnPassengersTimedOut(int count)
    {
        _passengersTimedOut += count;
        MarkReportsDirty(SyncDataType.GeneralReport, SyncDataType.OperationsReport);
    }

    // Transfers are counted in CompanyManager.TransferTripCount (see BusDriver); this just
    // refreshes the reports that display it.
    private void OnTransferRecorded()
    {
        MarkReportsDirty(SyncDataType.GeneralReport, SyncDataType.OperationsReport);
    }

    private void OnEmployeeHired(string employeeID)
    {
        _totalHires++;
        MarkReportsDirty(SyncDataType.HrReport);
    }

    private void SampleUtilization()
    {
        if (FleetManager.Instance == null) return;
        var buses = FleetManager.Instance.allBuses;
        if (buses == null || buses.Count == 0) return;

        int active = buses.Count(b => FleetManager.Instance.IsBusActive(b.BusID));
        _utilSampleSum += (active / (float)buses.Count) * 100f;
        _utilSampleCount++;
        MarkReportsDirty(SyncDataType.GeneralReport, SyncDataType.OperationsReport);
    }

    private void OnDayChanged()
    {
        // Daily heartbeat: refresh every report (Finance/Vendor have no per-event hook).
        MarkReportsDirty(NetworkSyncBroker.ReportTypes);
    }

    private void MarkReportsDirty(params SyncDataType[] types)
    {
        if (!IsServer) return;
        if (NetworkSyncBroker.Instance != null)
            foreach (var t in types) NetworkSyncBroker.Instance.MarkDirty(t);

        OnReportsUpdated?.Invoke(); // host UI refresh (local, no network)
    }

    /// <summary>
    /// Optional convenience for a game-over flow. The live snapshot already equals the final
    /// values, so this just forces an immediate refresh of every report.
    /// </summary>
    public void FinalizeReports()
    {
        if (IsServer) MarkReportsDirty(NetworkSyncBroker.ReportTypes);
    }

    // ============================================================
    // SNAPSHOT BUILDERS  (server builds live; client returns last received)
    // ============================================================

    public GeneralReportData GetGeneralReport() => IsServer ? BuildGeneralReport() : _generalReport;
    public OperationsReportData GetOperationsReport() => IsServer ? BuildOperationsReport() : _operationsReport;
    public MaintenanceReportData GetMaintenanceReport() => IsServer ? BuildMaintenanceReport() : _maintenanceReport;
    public HrReportData GetHrReport() => IsServer ? BuildHrReport() : _hrReport;
    public FinanceReportData GetFinanceReport() => IsServer ? BuildFinanceReport() : _financeReport;
    public VendorReportData GetVendorReport() => IsServer ? BuildVendorReport() : _vendorReport;

    private float Utilization() =>
        _utilSampleCount > 0 ? _utilSampleSum / _utilSampleCount : InstantUtilization();

    private float InstantUtilization()
    {
        if (FleetManager.Instance == null) return 0f;
        var buses = FleetManager.Instance.allBuses;
        if (buses == null || buses.Count == 0) return 0f;
        int active = buses.Count(b => FleetManager.Instance.IsBusActive(b.BusID));
        return (active / (float)buses.Count) * 100f;
    }

    private float OnTimePerformance()
    {
        int total = _passengersServed + _passengersTimedOut;
        return total > 0 ? (_passengersServed / (float)total) * 100f : 100f;
    }

    private float AvgWaitMinutes() =>
        _passengersServed > 0 ? (_totalWaitHours / _passengersServed) * 60f : 0f;

    private float AvgFleetHealth()
    {
        var buses = FleetManager.Instance != null ? FleetManager.Instance.allBuses : null;
        if (buses == null || buses.Count == 0) return 0f;
        float sum = 0f;
        foreach (var b in buses) sum += b.GetAverageHealth();
        return sum / buses.Count;
    }

    // Approximate reliability = average structural integrity (part MaxLife) across the fleet.
    private float FleetReliability()
    {
        var buses = FleetManager.Instance != null ? FleetManager.Instance.allBuses : null;
        if (buses == null || buses.Count == 0) return 100f;
        float sum = 0f; int partCount = 0;
        foreach (var b in buses)
        {
            if (b.Parts == null) continue;
            foreach (var p in b.Parts) { sum += p.MaxLife; partCount++; }
        }
        return partCount > 0 ? sum / partCount : 100f;
    }

    private int BusesNeedingRepair()
    {
        var buses = FleetManager.Instance != null ? FleetManager.Instance.allBuses : null;
        if (buses == null) return 0;
        float threshold = MaintenanceManager.Instance != null
            ? MaintenanceManager.Instance.operationalThreshold : 30f;
        return buses.Count(b => b.GetAverageHealth() < threshold);
    }

    // Buses ready for service: above the operational threshold and not currently broken down.
    private int AvailableBuses()
    {
        var buses = FleetManager.Instance != null ? FleetManager.Instance.allBuses : null;
        if (buses == null) return 0;
        float threshold = MaintenanceManager.Instance != null
            ? MaintenanceManager.Instance.operationalThreshold : 30f;
        var mm = MaintenanceManager.Instance;
        return buses.Count(b => b.GetAverageHealth() >= threshold && (mm == null || !mm.IsOnRouteBreakdown(b.BusID)));
    }

    // Traffic-light system status: 0 = Normal, 1 = Warning, 2 = Critical.
    private int ComputeSystemStatus()
    {
        var buses = FleetManager.Instance != null ? FleetManager.Instance.allBuses : null;
        float breakdownThreshold = MaintenanceManager.Instance != null
            ? MaintenanceManager.Instance.breakdownThreshold : 5f;

        bool anyDown = false;
        if (buses != null)
        {
            var mm = MaintenanceManager.Instance;
            foreach (var b in buses)
            {
                if (b.GetAverageHealth() <= breakdownThreshold || (mm != null && mm.IsOnRouteBreakdown(b.BusID)))
                {
                    anyDown = true;
                    break;
                }
            }
        }

        float satisfaction = CompanyManager.Instance != null ? CompanyManager.Instance.GlobalSatisfaction : 100f;

        if (anyDown || FleetReliability() < 40f) return 2;                                  // Critical
        if (BusesNeedingRepair() > 0 || satisfaction < 50f || Utilization() < 30f) return 1; // Warning
        return 0;                                                                            // Normal
    }

    private GeneralReportData BuildGeneralReport()
    {
        return new GeneralReportData
        {
            customerSatisfaction = CompanyManager.Instance != null ? CompanyManager.Instance.GlobalSatisfaction : 0f,
            onTimePerformance = OnTimePerformance(),
            avgWaitMinutes = AvgWaitMinutes(),
            transfers = CompanyManager.Instance != null ? CompanyManager.Instance.GetCompanyData().TransferTripCount : 0,
            totalBreakdowns = _totalBreakdowns,
            fleetReliability = FleetReliability(),
            fleetUtilization = Utilization(),
            cashBalance = CompanyManager.Instance != null ? CompanyManager.Instance.GetCompanyData().CurrentBalance : 0f,
            systemStatus = ComputeSystemStatus()
        };
    }

    private OperationsReportData BuildOperationsReport()
    {
        return new OperationsReportData
        {
            onTimePerformance = OnTimePerformance(),
            avgWaitMinutes = AvgWaitMinutes(),
            transfers = CompanyManager.Instance != null ? CompanyManager.Instance.GetCompanyData().TransferTripCount : 0,
            fleetUtilization = Utilization(),
            passengersServed = _passengersServed,
            passengersMissed = _passengersTimedOut,
            availableBuses = AvailableBuses(),
            stopCoverage = TransportManager.Instance != null ? TransportManager.Instance.StopCoveragePercent() : -1f,
            longestRouteStops = TransportManager.Instance != null ? TransportManager.Instance.LongestRouteStopCount() : 0,
            stopsNotCovered = TransportManager.Instance != null ? TransportManager.Instance.StopsNotCovered() : 0
        };
    }

    private MaintenanceReportData BuildMaintenanceReport()
    {
        var mm = MaintenanceManager.Instance;

        // Breakdown frequency = breakdowns per elapsed day.
        int day = SimulationTimeManager.Instance != null ? SimulationTimeManager.Instance.CurrentDay : 1;
        float breakdownFrequency = _totalBreakdowns / (float)Mathf.Max(1, day);

        // Repair completion rate = breakdowns returned to service / breakdowns occurred.
        int resolved = mm != null ? mm.BreakdownsResolved : 0;
        float repairCompletionRate = _totalBreakdowns > 0 ? (resolved / (float)_totalBreakdowns) * 100f : -1f;

        return new MaintenanceReportData
        {
            totalBreakdowns = _totalBreakdowns,
            repairsCompleted = _repairsCompleted,
            partsReplaced = _partsReplaced,
            fleetReliability = FleetReliability(),
            avgFleetHealth = AvgFleetHealth(),
            busesNeedingRepair = BusesNeedingRepair(),
            availableBuses = AvailableBuses(),
            mttrHours = mm != null ? mm.AverageRepairHours : -1f,
            technicianUtilization = mm != null ? mm.TechnicianUtilization : -1f,
            breakdownFrequency = breakdownFrequency,
            repairCompletionRate = repairCompletionRate,
            sparePartDelays = mm != null ? mm.SparePartDelays : 0
        };
    }

    private HrReportData BuildHrReport()
    {
        var emp = EmployeeManager.Instance;
        if (emp == null || emp.allEmployees == null || emp.allEmployees.Count == 0)
            return new HrReportData { totalHires = _totalHires, avgFatigue = -1f, inTraining = -1 };

        var staff = emp.allEmployees;
        float weeklyPayroll = staff.Sum(e => e.WeeklySalary) + staff.Count * emp.upkeepPerEmployee;
        int teamCount = staff
            .Where(e => !string.IsNullOrEmpty(e.AssignedTeamID))
            .Select(e => e.AssignedTeamID)
            .Distinct()
            .Count();

        return new HrReportData
        {
            totalEmployees = staff.Count,
            totalHires = _totalHires,
            avgSkill = staff.Average(e => e.SkillLevel),
            weeklyPayroll = weeklyPayroll,
            teamCount = teamCount,
            avgFatigue = -1f, // TODO: fatigue/overtime not modeled yet (doc: 1 pt / 2 sim-min overtime, morale = 100 - fatigue)
            inTraining = -1   // TODO: training queue not modeled yet
        };
    }

    private FinanceReportData BuildFinanceReport()
    {
        var data = new FinanceReportData();
        if (CompanyManager.Instance == null) return data;

        var company = CompanyManager.Instance.GetCompanyData();
        data.cashBalance = company.CurrentBalance;

        if (company.History != null)
        {
            foreach (var tx in company.History)
            {
                if (tx.Amount >= 0)
                {
                    data.totalRevenue += tx.Amount;
                }
                else
                {
                    float spend = -tx.Amount;
                    data.totalExpenses += spend;
                    if (tx.Category == TransactionCategory.StaffSalary || tx.Category == TransactionCategory.StaffUpkeep)
                        data.payrollSpend += spend;
                    else if (tx.Category == TransactionCategory.PartPurchase)
                        data.partsSpend += spend;
                }
            }
        }

        // Procurement / vendor KPIs (orders are the Finance Manager's responsibility).
        var vm = VendorManager.Instance;
        if (vm != null)
        {
            data.ordersPlaced = vm.lifetimeOrdersPlaced;
            data.onTimeDeliveryRate = vm.lifetimeOrdersDelivered > 0
                ? (vm.lifetimeOnTimeDeliveries / (float)vm.lifetimeOrdersDelivered) * 100f : 0f;
            data.avgPartQuality = vm.lifetimeOrdersDelivered > 0
                ? vm.lifetimeQualitySum / vm.lifetimeOrdersDelivered : 0f;
        }

        return data;
    }

    private VendorReportData BuildVendorReport()
    {
        var vm = VendorManager.Instance;
        if (vm == null) return new VendorReportData();

        return new VendorReportData
        {
            ordersPlaced = vm.lifetimeOrdersPlaced,
            ordersDelivered = vm.lifetimeOrdersDelivered,
            onTimeDeliveryRate = vm.lifetimeOrdersDelivered > 0
                ? (vm.lifetimeOnTimeDeliveries / (float)vm.lifetimeOrdersDelivered) * 100f : 0f,
            avgPartQuality = vm.lifetimeOrdersDelivered > 0
                ? vm.lifetimeQualitySum / vm.lifetimeOrdersDelivered : 0f,
            activeDeals = vm.activeDeals != null ? vm.activeDeals.Count : 0
        };
    }

    // ============================================================
    // NETWORK SYNC  (server -> subscribed clients)
    // ============================================================

    private void PerformReportSync(SyncDataType type, BaseRpcTarget target)
    {
        switch (type)
        {
            case SyncDataType.GeneralReport:
                SyncGeneralReportRpc(JsonUtility.ToJson(BuildGeneralReport()), target); break;
            case SyncDataType.OperationsReport:
                SyncOperationsReportRpc(JsonUtility.ToJson(BuildOperationsReport()), target); break;
            case SyncDataType.MaintenanceReport:
                SyncMaintenanceReportRpc(JsonUtility.ToJson(BuildMaintenanceReport()), target); break;
            case SyncDataType.HrReport:
                SyncHrReportRpc(JsonUtility.ToJson(BuildHrReport()), target); break;
            case SyncDataType.FinanceReport:
                SyncFinanceReportRpc(JsonUtility.ToJson(BuildFinanceReport()), target); break;
            case SyncDataType.VendorReport:
                SyncVendorReportRpc(JsonUtility.ToJson(BuildVendorReport()), target); break;
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void SyncGeneralReportRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        _generalReport = JsonUtility.FromJson<GeneralReportData>(json);
        OnReportsUpdated?.Invoke();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void SyncOperationsReportRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        _operationsReport = JsonUtility.FromJson<OperationsReportData>(json);
        OnReportsUpdated?.Invoke();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void SyncMaintenanceReportRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        _maintenanceReport = JsonUtility.FromJson<MaintenanceReportData>(json);
        OnReportsUpdated?.Invoke();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void SyncHrReportRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        _hrReport = JsonUtility.FromJson<HrReportData>(json);
        OnReportsUpdated?.Invoke();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void SyncFinanceReportRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        _financeReport = JsonUtility.FromJson<FinanceReportData>(json);
        OnReportsUpdated?.Invoke();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void SyncVendorReportRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        _vendorReport = JsonUtility.FromJson<VendorReportData>(json);
        OnReportsUpdated?.Invoke();
    }
}
