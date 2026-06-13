using System;
using System.Collections.Generic;
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

    // Mirrors the demand-met % (served / total) to every client so a live panel (e.g. the GM panel)
    // can show it without subscribing to the heavy report snapshots. Written by the server.
    private readonly NetworkVariable<float> _netDemandMet = new NetworkVariable<float>(
        100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Percentage of passenger demand met (boarded vs boarded+gave-up). Server computes it live;
    /// clients read the networked mirror.
    /// </summary>
    public float DemandMetPercent => IsServer ? OnTimePerformance() : _netDemandMet.Value;

    // --- Client-received snapshots (populated by RPCs on non-server peers) ---
    private GeneralReportData _generalReport;
    private OperationsReportData _operationsReport;
    private MaintenanceReportData _maintenanceReport;
    private HrReportData _hrReport;
    private FinanceReportData _financeReport;
    private VendorReportData _vendorReport;

    /// <summary>Fired whenever any report data changes (host: counters; client: RPC received).</summary>
    public event Action OnReportsUpdated;

    // --- KPI drill-down detail (server logs events; clients fetch on demand) ---
    private const int DetailLogCap = 150;
    private readonly List<KpiDetailEntry> _breakdownLog = new List<KpiDetailEntry>();
    private readonly List<KpiDetailEntry> _otpLog = new List<KpiDetailEntry>();      // boarded / gave-up timeline
    private readonly List<KpiDetailEntry> _waitingLog = new List<KpiDetailEntry>();  // per-batch wait times
    private readonly List<KpiDetailEntry> _hireLog = new List<KpiDetailEntry>();     // hires timeline

    /// <summary>Fired when a requested KPI detail payload is ready (host: built locally; client: RPC received).</summary>
    public event Action<KpiDetailData> OnKpiDetailReady;

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

        // Log the event for the Total Breakdowns drill-down (which bus, what failed, when).
        var time = SimulationTimeManager.Instance;
        _breakdownLog.Insert(0, new KpiDetailEntry
        {
            label = $"Bus {busID} — {reason} failure",
            value = 0f,
            day = time != null ? time.CurrentDay : 0,
            timeOfDay = time != null ? time.CurrentTimeOfDay : 0f,
            kind = 2
        });
        if (_breakdownLog.Count > DetailLogCap)
            _breakdownLog.RemoveAt(_breakdownLog.Count - 1);

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

        // Drill-down timelines: On-Time Performance (boarded) and Average Waiting Time (wait per batch).
        PushDetail(_otpLog, $"{count} boarded", count, 1);
        PushDetail(_waitingLog, $"{count} boarded — waited {waitHours * 60f:F0} min", waitHours * 60f, 0);

        PublishDemandMet();
        MarkReportsDirty(SyncDataType.GeneralReport, SyncDataType.OperationsReport);
    }

    private void OnPassengersTimedOut(int count)
    {
        _passengersTimedOut += count;

        // On-Time Performance drill-down: the give-ups that drag the ratio down.
        PushDetail(_otpLog, $"{count} gave up waiting", count, 2);

        PublishDemandMet();
        MarkReportsDirty(SyncDataType.GeneralReport, SyncDataType.OperationsReport);
    }

    // Appends a stamped entry to a capped, newest-first drill-down log.
    private void PushDetail(List<KpiDetailEntry> log, string label, float value, int kind)
    {
        var time = SimulationTimeManager.Instance;
        log.Insert(0, new KpiDetailEntry
        {
            label = label,
            value = value,
            day = time != null ? time.CurrentDay : 0,
            timeOfDay = time != null ? time.CurrentTimeOfDay : 0f,
            kind = kind
        });
        if (log.Count > DetailLogCap) log.RemoveAt(log.Count - 1);
    }

    // Server-only: mirror the live demand-met % to clients.
    private void PublishDemandMet()
    {
        if (IsServer && IsSpawned) _netDemandMet.Value = OnTimePerformance();
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

        // Hire-timeline drill-down: look up the freshly-added employee for name/skill.
        var em = EmployeeManager.Instance;
        var emp = em?.allEmployees?.FirstOrDefault(e => e.EmployeeID == employeeID);
        string who = emp != null ? $"Hired {emp.FullName} (skill {emp.SkillLevel:F0})" : "Hired employee";
        PushDetail(_hireLog, who, emp != null ? emp.SkillLevel : 0f, 1);

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
        // HrReport included so the hourly-updated fatigue KPI refreshes live.
        MarkReportsDirty(SyncDataType.GeneralReport, SyncDataType.OperationsReport, SyncDataType.HrReport);
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

        return new MaintenanceReportData
        {
            totalBreakdowns = _totalBreakdowns,
            repairsCompleted = _repairsCompleted,
            partsReplaced = _partsReplaced,
            fleetReliability = FleetReliability(),
            avgFleetHealth = AvgFleetHealth(),
            busesNeedingRepair = BusesNeedingRepair(),
            availableBuses = AvailableBuses(),
            mttrHours = mm != null ? mm.AverageRepairHours : -1f,           // average repair duration
            technicianUtilization = mm != null ? mm.TechnicianUtilization : -1f,
            breakdownFrequency = breakdownFrequency,
            repairCompletionRate = mm != null ? mm.OnTimeRepairRate : -1f,   // on-time repair % (within target)
            sparePartDelays = mm != null ? mm.SparePartDelays : 0,
            totalDowntimeHours = mm != null ? mm.TotalDowntimeHours : 0f      // Bus Return to Service (total)
        };
    }

    private HrReportData BuildHrReport()
    {
        var emp = EmployeeManager.Instance;
        if (emp == null || emp.allEmployees == null || emp.allEmployees.Count == 0)
            return new HrReportData { totalHires = _totalHires, avgFatigue = -1f, inTraining = 0 };

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
            avgFatigue = staff.Average(e => e.Fatigue),       // 0 = rested, 100 = burnt out
            inTraining = staff.Count(e => e.IsInTraining)
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
    // KPI DRILL-DOWN DETAIL  (on-demand request/response)
    // ============================================================

    /// <summary>
    /// Fetch the detail payload behind a General Report card. The host builds it locally and
    /// fires <see cref="OnKpiDetailReady"/> immediately; a client round-trips to the server.
    /// </summary>
    public void RequestKpiDetail(KpiMetric metric)
    {
        if (IsServer) OnKpiDetailReady?.Invoke(BuildKpiDetail(metric));
        else RequestKpiDetailRpc((int)metric);
    }

    [Rpc(SendTo.Server)]
    private void RequestKpiDetailRpc(int metric, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        string json = JsonUtility.ToJson(BuildKpiDetail((KpiMetric)metric));
        SendKpiDetailRpc(json, RpcTarget.Single(sender, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void SendKpiDetailRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return;
        OnKpiDetailReady?.Invoke(JsonUtility.FromJson<KpiDetailData>(json));
    }

    // Server-side: assemble the detail payload for one metric from the owning manager's log.
    private KpiDetailData BuildKpiDetail(KpiMetric metric)
    {
        var entries = new System.Collections.Generic.List<KpiDetailEntry>();
        string header = "";
        string explanation = "No detail data yet.";

        switch (metric)
        {
            case KpiMetric.CustomerSatisfaction:
                header = $"%{(CompanyManager.Instance != null ? CompanyManager.Instance.GlobalSatisfaction : 0f):F0}";
                explanation = "Each delivered passenger raises it; each give-up lowers it.";
                if (CompanyManager.Instance != null)
                    entries.AddRange(CompanyManager.Instance.SatisfactionLog);
                break;

            case KpiMetric.TotalBreakdowns:
                header = $"{_totalBreakdowns}";
                explanation = "Every bus that suffered a critical part failure.";
                entries.AddRange(_breakdownLog);
                break;

            case KpiMetric.OnTimePerformance:
                header = $"%{OnTimePerformance():F0}";
                explanation = "Boarded vs. (boarded + gave up). Green boarded, red gave up.";
                entries.AddRange(_otpLog);
                break;

            case KpiMetric.AvgWaitingTime:
                header = $"{AvgWaitMinutes():F0} min";
                explanation = "Wait time of each boarded batch (value in minutes).";
                entries.AddRange(_waitingLog);
                break;

            case KpiMetric.Transfers:
                header = $"{(CompanyManager.Instance != null ? CompanyManager.Instance.GetCompanyData().TransferTripCount : 0)}";
                explanation = "Passengers who changed buses to finish a journey.";
                if (CompanyManager.Instance != null)
                    entries.AddRange(CompanyManager.Instance.TransferLog);
                break;

            case KpiMetric.FleetReliability:
                header = $"%{FleetReliability():F0}";
                explanation = "Average structural integrity of each bus's parts.";
                entries.AddRange(BuildFleetReliabilityEntries());
                break;

            case KpiMetric.FleetUtilization:
                header = $"%{Utilization():F0}";
                explanation = "Each bus's current status — on a route or idle.";
                entries.AddRange(BuildFleetUtilizationEntries());
                break;

            case KpiMetric.FinanceTotalRevenue:
                header = ReportFormat.Money(BuildFinanceReport().totalRevenue);
                explanation = "Every income line in the ledger.";
                entries.AddRange(BuildLedgerEntries(t => t.Amount >= 0f));
                break;

            case KpiMetric.FinanceTotalExpenses:
                header = ReportFormat.Money(BuildFinanceReport().totalExpenses);
                explanation = "Every expense line in the ledger.";
                entries.AddRange(BuildLedgerEntries(t => t.Amount < 0f));
                break;

            case KpiMetric.FinancePayrollSpend:
                header = ReportFormat.Money(BuildFinanceReport().payrollSpend);
                explanation = "Staff salary and upkeep payments.";
                entries.AddRange(BuildLedgerEntries(t =>
                    t.Category == TransactionCategory.StaffSalary || t.Category == TransactionCategory.StaffUpkeep));
                break;

            case KpiMetric.FinancePartsSpend:
                header = ReportFormat.Money(BuildFinanceReport().partsSpend);
                explanation = "Spare-part purchases from vendors.";
                entries.AddRange(BuildLedgerEntries(t => t.Category == TransactionCategory.PartPurchase));
                break;

            case KpiMetric.FinanceOrdersPlaced:
                header = $"{(VendorManager.Instance != null ? VendorManager.Instance.lifetimeOrdersPlaced : 0)}";
                explanation = "Every parts order placed with a vendor.";
                if (VendorManager.Instance != null) entries.AddRange(VendorManager.Instance.OrderLog);
                break;

            case KpiMetric.FinanceOnTimeDeliveries:
                header = $"%{BuildFinanceReport().onTimeDeliveryRate:F0}";
                explanation = "Each delivery — on time (green) or late (red).";
                if (VendorManager.Instance != null) entries.AddRange(VendorManager.Instance.DeliveryLog);
                break;

            case KpiMetric.FinanceAvgPartQuality:
                header = ReportFormat.Score(BuildFinanceReport().avgPartQuality);
                explanation = "Durability (quality) of each delivered part batch.";
                if (VendorManager.Instance != null) entries.AddRange(VendorManager.Instance.DeliveryLog);
                break;

            case KpiMetric.OpsPassengersServed:
                header = $"{_passengersServed}";
                explanation = "Each batch of passengers that boarded.";
                entries.AddRange(_otpLog.Where(e => e.kind == 1));
                break;

            case KpiMetric.OpsPassengersMissed:
                header = $"{_passengersTimedOut}";
                explanation = "Each batch of passengers that gave up waiting.";
                entries.AddRange(_otpLog.Where(e => e.kind == 2));
                break;

            case KpiMetric.OpsAvailableBuses:
                header = $"{AvailableBuses()}";
                explanation = "Each bus — ready for service (green) or not (red).";
                entries.AddRange(BuildAvailableBusesEntries());
                break;

            case KpiMetric.OpsStopCoverage:
                header = ReportFormat.Pct(TransportManager.Instance != null ? TransportManager.Instance.StopCoveragePercent() : -1f);
                explanation = "Each stop — served by a route (green) or not (red).";
                entries.AddRange(BuildStopCoverageEntries(false));
                break;

            case KpiMetric.OpsLongestRoute:
                header = $"{(TransportManager.Instance != null ? TransportManager.Instance.LongestRouteStopCount() : 0)} stops";
                explanation = "Every route and its stop sequence (longest first).";
                entries.AddRange(BuildLongestRouteEntries());
                break;

            case KpiMetric.OpsStopsNotCovered:
                header = $"{(TransportManager.Instance != null ? TransportManager.Instance.StopsNotCovered() : 0)}";
                explanation = "Stops not served by any active route.";
                entries.AddRange(BuildStopCoverageEntries(true));
                break;

            case KpiMetric.HrTotalEmployees:
                header = $"{BuildHrReport().totalEmployees}";
                explanation = "Every employee on the payroll (in training shown in grey).";
                entries.AddRange(BuildEmployeeEntries(e => new KpiDetailEntry
                {
                    label = $"{e.FullName} — skill {e.SkillLevel:F0}{(string.IsNullOrEmpty(e.AssignedTeamID) ? "" : $", {e.AssignedTeamID}")}{(e.IsInTraining ? " (in training)" : "")}",
                    value = 0f,
                    kind = e.IsInTraining ? 0 : 1
                }));
                break;

            case KpiMetric.HrTotalHires:
                header = $"{_totalHires}";
                explanation = "Every employee hired this session.";
                entries.AddRange(_hireLog);
                break;

            case KpiMetric.HrAvgSkill:
                header = ReportFormat.Score(BuildHrReport().avgSkill);
                explanation = "Each employee's skill level.";
                entries.AddRange(BuildEmployeeEntries(e => new KpiDetailEntry
                {
                    label = e.FullName,
                    value = e.SkillLevel,
                    kind = e.SkillLevel >= 70f ? 1 : e.SkillLevel < 40f ? 2 : 0
                }));
                break;

            case KpiMetric.HrWeeklyPayroll:
                header = ReportFormat.Money(BuildHrReport().weeklyPayroll);
                explanation = "Each employee's weekly wage (plus shared upkeep).";
                entries.AddRange(BuildEmployeeEntries(e => new KpiDetailEntry
                {
                    label = $"{e.FullName} — ${e.WeeklySalary:N0}/wk",
                    value = e.WeeklySalary,
                    kind = 0
                }));
                break;

            case KpiMetric.HrTeamCount:
                header = $"{BuildHrReport().teamCount}";
                explanation = "Each maintenance team and its pooled skill.";
                entries.AddRange(BuildTeamEntries());
                break;

            case KpiMetric.HrAvgFatigue:
                header = ReportFormat.Score(BuildHrReport().avgFatigue);
                explanation = "Each employee's fatigue — rested (green) to burnt out (red).";
                entries.AddRange(BuildEmployeeEntries(e => new KpiDetailEntry
                {
                    label = $"{e.FullName}{(e.IsInTraining ? " (resting)" : "")}",
                    value = e.Fatigue,
                    kind = e.Fatigue >= 66f ? 2 : e.Fatigue <= 33f ? 1 : 0
                }));
                break;

            case KpiMetric.MaintMttr:
                header = ReportFormat.Hours(MaintenanceManager.Instance != null ? MaintenanceManager.Instance.AverageRepairHours : -1f);
                explanation = "Each repair's duration (green = within target, red = over). MTTR is their average.";
                if (MaintenanceManager.Instance != null) entries.AddRange(MaintenanceManager.Instance.RepairLog);
                break;

            case KpiMetric.MaintBusReturnToService:
                header = ReportFormat.Hours(MaintenanceManager.Instance != null ? MaintenanceManager.Instance.TotalDowntimeHours : 0f);
                explanation = "Total hours buses spent out of service — the sum of every repair's duration.";
                if (MaintenanceManager.Instance != null) entries.AddRange(MaintenanceManager.Instance.RepairLog);
                break;

            case KpiMetric.MaintBreakdownFrequency:
                header = ReportFormat.PerDay(BuildMaintenanceReport().breakdownFrequency);
                explanation = "Every breakdown that occurred (frequency = these ÷ days elapsed).";
                entries.AddRange(_breakdownLog);
                break;

            case KpiMetric.MaintRepairCompletionRate:
                header = ReportFormat.Pct(MaintenanceManager.Instance != null ? MaintenanceManager.Instance.OnTimeRepairRate : -1f);
                explanation = "Each repair — finished within the target time (green) or over (red). Rate = on-time ÷ all.";
                if (MaintenanceManager.Instance != null) entries.AddRange(MaintenanceManager.Instance.RepairLog);
                break;

            case KpiMetric.MaintSparePartDelays:
                header = $"{(MaintenanceManager.Instance != null ? MaintenanceManager.Instance.SparePartDelays : 0)}";
                explanation = "Each time a repair stalled waiting for a spare part.";
                if (MaintenanceManager.Instance != null) entries.AddRange(MaintenanceManager.Instance.DelayLog);
                break;

            case KpiMetric.MaintTechnicianUtilization:
                header = ReportFormat.Pct(MaintenanceManager.Instance != null ? MaintenanceManager.Instance.TechnicianUtilization : -1f);
                explanation = "Each technician's crew utilization (busy hours ÷ on-shift hours).";
                entries.AddRange(BuildEmployeeEntries(e =>
                {
                    if (e.IsInTraining)
                        return new KpiDetailEntry { label = $"{e.FullName} — in training", value = 0f, kind = 0 };
                    if (string.IsNullOrEmpty(e.AssignedDepotID))
                        return new KpiDetailEntry { label = $"{e.FullName} — unassigned", value = 0f, kind = 2 };

                    float u = MaintenanceManager.Instance != null
                        ? MaintenanceManager.Instance.GetTechnicianUtilization(e.AssignedDepotID, e.AssignedTeamID) : 0f;
                    return new KpiDetailEntry
                    {
                        label = $"{e.FullName} ({e.AssignedTeamID}) — {u:F0}% utilized",
                        value = u,
                        kind = u >= 50f ? 1 : u > 0f ? 0 : 2
                    };
                }));
                break;
        }

        // Group by colour (green → grey → red) for a cleaner look. OrderBy is stable, so the
        // existing order within each colour (e.g. newest-first on timelines) is preserved.
        entries = entries.OrderBy(e => KindOrder(e.kind)).ToList();

        return new KpiDetailData
        {
            metric = (int)metric,
            headerValue = header,
            explanation = explanation,
            entries = entries
        };
    }

    // Sort weight: positive (green) first, neutral (grey) middle, negative (red) last.
    private static int KindOrder(int kind) => kind == 1 ? 0 : kind == 0 ? 1 : 2;

    // Live per-bus reliability: each bus's average part structural integrity (MaxLife).
    private List<KpiDetailEntry> BuildFleetReliabilityEntries()
    {
        var list = new List<KpiDetailEntry>();
        var buses = FleetManager.Instance != null ? FleetManager.Instance.allBuses : null;
        if (buses == null) return list;

        foreach (var b in buses)
        {
            if (b.Parts == null || b.Parts.Count == 0) continue;
            float sum = 0f;
            foreach (var p in b.Parts) sum += p.MaxLife;
            float rel = sum / b.Parts.Count;
            list.Add(new KpiDetailEntry
            {
                label = $"Bus {b.BusID}",
                value = rel,
                kind = rel >= 70f ? 1 : rel < 40f ? 2 : 0
            });
        }
        return list;
    }

    // Live per-bus utilization: whether each bus is currently active on a route.
    private List<KpiDetailEntry> BuildFleetUtilizationEntries()
    {
        var list = new List<KpiDetailEntry>();
        var fm = FleetManager.Instance;
        if (fm == null || fm.allBuses == null) return list;

        foreach (var b in fm.allBuses)
        {
            bool active = fm.IsBusActive(b.BusID);
            list.Add(new KpiDetailEntry
            {
                label = $"Bus {b.BusID} — {(active ? "On route" : "Idle")}",
                value = 0f,
                kind = active ? 1 : 0
            });
        }
        return list;
    }

    // Newest-first ledger lines matching a filter (income green / expense red).
    private List<KpiDetailEntry> BuildLedgerEntries(Func<Transaction, bool> filter)
    {
        var list = new List<KpiDetailEntry>();
        var company = CompanyManager.Instance != null ? CompanyManager.Instance.GetCompanyData() : null;
        if (company?.History == null) return list;

        var history = company.History;
        for (int i = history.Count - 1; i >= 0 && list.Count < DetailLogCap; i--)
        {
            var tx = history[i];
            if (!filter(tx)) continue;
            list.Add(new KpiDetailEntry
            {
                label = tx.Count > 1 ? $"{tx.Description} (x{tx.Count})" : tx.Description,
                value = tx.Amount,
                day = tx.Timestamp,          // sim day
                timeOfDay = 0f,
                kind = tx.Amount >= 0f ? 1 : 2
            });
        }
        return list;
    }

    // Live per-bus availability: ready for service vs. needs repair / broken down.
    private List<KpiDetailEntry> BuildAvailableBusesEntries()
    {
        var list = new List<KpiDetailEntry>();
        var fm = FleetManager.Instance;
        if (fm == null || fm.allBuses == null) return list;

        float threshold = MaintenanceManager.Instance != null ? MaintenanceManager.Instance.operationalThreshold : 30f;
        var mm = MaintenanceManager.Instance;

        foreach (var b in fm.allBuses)
        {
            bool down = mm != null && mm.IsOnRouteBreakdown(b.BusID);
            float health = b.GetAverageHealth();
            bool available = health >= threshold && !down;
            list.Add(new KpiDetailEntry
            {
                label = $"Bus {b.BusID} — {(available ? "Available" : down ? "Broken down" : "Needs repair")} ({health:F0}%)",
                value = 0f,
                kind = available ? 1 : 2
            });
        }
        return list;
    }

    // Live per-stop coverage. onlyUncovered = list just the stops served by no route.
    private List<KpiDetailEntry> BuildStopCoverageEntries(bool onlyUncovered)
    {
        var list = new List<KpiDetailEntry>();
        var tm = TransportManager.Instance;
        if (tm == null) return list;

        var served = tm.GetServedStopIDs();
        foreach (var stop in tm.RegisteredStops)
        {
            if (stop == null) continue;
            bool covered = served.Contains(stop.stopID);
            if (onlyUncovered && covered) continue;
            list.Add(new KpiDetailEntry
            {
                label = onlyUncovered ? $"Stop {stop.stopID}" : $"Stop {stop.stopID} — {(covered ? "Covered" : "Not covered")}",
                value = 0f,
                kind = covered ? 1 : 2
            });
        }
        return list;
    }

    // Every active route with its stop sequence, longest first.
    private List<KpiDetailEntry> BuildLongestRouteEntries()
    {
        var list = new List<KpiDetailEntry>();
        var tm = TransportManager.Instance;
        if (tm == null || tm.ActiveRoutes == null) return list;

        foreach (var route in tm.ActiveRoutes.OrderByDescending(r => r?.StopIDs?.Count ?? 0))
        {
            if (route == null) continue;
            int n = route.StopIDs != null ? route.StopIDs.Count : 0;
            string seq = n > 0 ? string.Join(" → ", route.StopIDs) : "(no stops)";
            list.Add(new KpiDetailEntry
            {
                label = $"{route.RouteName} ({n} stops): {seq}",
                value = 0f,
                kind = 0
            });
        }
        return list;
    }

    // One entry per employee, mapped by the caller (used by the HR drill-downs).
    private List<KpiDetailEntry> BuildEmployeeEntries(Func<EmployeeData, KpiDetailEntry> map)
    {
        var list = new List<KpiDetailEntry>();
        var em = EmployeeManager.Instance;
        if (em == null || em.allEmployees == null) return list;
        foreach (var e in em.allEmployees) list.Add(map(e));
        return list;
    }

    // One entry per maintenance team: member count and pooled skill.
    private List<KpiDetailEntry> BuildTeamEntries()
    {
        var list = new List<KpiDetailEntry>();
        var em = EmployeeManager.Instance;
        if (em == null || em.allEmployees == null) return list;

        var groups = em.allEmployees
            .Where(e => !string.IsNullOrEmpty(e.AssignedTeamID))
            .GroupBy(e => e.AssignedTeamID);

        foreach (var g in groups)
        {
            int members = g.Count();
            float skill = g.Sum(m => m.SkillLevel);
            list.Add(new KpiDetailEntry
            {
                label = $"{g.Key} — {members} member{(members == 1 ? "" : "s")}, pooled skill {skill:F0}",
                value = members,
                kind = 0
            });
        }
        return list;
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
