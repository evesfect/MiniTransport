using System;
using System.Collections.Generic;

[Flags]
public enum SyncDataType
{
    None = 0,
    CompanyStats = 1 << 0,
    FleetStats = 1 << 1,
    MaintenanceStats = 1 << 2,
    CompanyLedger = 1 << 3,

    // --- End-of-game report snapshots (aggregated by KPIManager) ---
    GeneralReport = 1 << 4,
    OperationsReport = 1 << 5,
    MaintenanceReport = 1 << 6,
    HrReport = 1 << 7,
    FinanceReport = 1 << 8,
    VendorReport = 1 << 9
}

[Serializable]
public struct CompanyStatsData
{
    public float currentBalance;
    public int transferTripCount; // Global KPI: Number of Transfer Trips
}

[Serializable]
public struct FleetStatsData
{
    public int totalBuses;
    public int lowDurabilityBuses;
}

[Serializable]
public struct BusHealthData
{
    public string busID;
    public float durability;
}

[Serializable]
public struct MaintenanceStatsData
{
    public float operationalThreshold;
    public float breakdownThreshold;
    public List<BusHealthData> busHealthList;
}

[Serializable]
public struct CompanyLedgerData
{
    public List<Transaction> transactions;
}

// ============================================================================
// END-OF-GAME REPORT SNAPSHOTS
// Flat value structs (ints/floats only) so JSON payloads stay tiny. Built on
// the server by KPIManager, synced only to clients who opened the panel.
// ============================================================================

[Serializable]
public struct GeneralReportData
{
    public float customerSatisfaction; // 0-100
    public float onTimePerformance;    // 0-100 (approx: served / (served+missed))
    public float avgWaitMinutes;
    public int transfers;              // stub until passenger journey identity exists
    public int totalBreakdowns;        // doc: "Failure Count"
    public float fleetReliability;     // 0-100 (approx: avg structural integrity)
    public float fleetUtilization;     // 0-100 (avg active/total over time)
    public float cashBalance;
    public int systemStatus;           // 0 = Normal, 1 = Warning, 2 = Critical (traffic light)
}

[Serializable]
public struct OperationsReportData
{
    public float onTimePerformance;
    public float avgWaitMinutes;
    public int transfers;              // stub
    public float fleetUtilization;
    public int passengersServed;
    public int passengersMissed;       // gave up waiting (timed out)
    public int availableBuses;         // buses ready for service (not broken / above op threshold)
    public float stopCoverage;         // % of stops served by >=1 route (-1 = no stops)
    public int longestRouteStops;      // highest stop count on any single active route
    public int stopsNotCovered;        // registered stops served by no route
}

[Serializable]
public struct MaintenanceReportData
{
    public int totalBreakdowns;        // doc: "Failure Count"
    public int repairsCompleted;
    public int partsReplaced;
    public float fleetReliability;
    public float avgFleetHealth;
    public int busesNeedingRepair;
    public int availableBuses;         // doc: usable buses ready for service
    public float mttrHours;            // Mean Time To Repair = average repair duration (hours; -1 = no data)
    public float technicianUtilization;// crew workload vs capacity (0-100; -1 = no crews)
    public float breakdownFrequency;   // breakdowns per day
    public float repairCompletionRate; // On-time repair rate: % of repairs finished within target time (-1 = no repairs yet)
    public int sparePartDelays;        // spare-part delay frequency: count of "waiting for parts" stall episodes
    public float totalDowntimeHours;   // Bus Return to Service: sum of all repair durations across the session (hours)
}

[Serializable]
public struct HrReportData
{
    public int totalEmployees;
    public int totalHires;
    public float avgSkill;
    public float weeklyPayroll;
    public int teamCount;
    public float avgFatigue;           // STUB (-1): overtime fatigue not modeled yet
    public int inTraining;             // STUB (-1): training queue not modeled yet
}

[Serializable]
public struct FinanceReportData
{
    public float cashBalance;
    public float totalRevenue;
    public float totalExpenses;
    public float payrollSpend;
    public float partsSpend;
    // Procurement / vendor KPIs (orders are the Finance Manager's responsibility).
    public int ordersPlaced;
    public float onTimeDeliveryRate;   // 0-100
    public float avgPartQuality;       // 0-100 (avg durability of delivered parts)
}

[Serializable]
public struct VendorReportData
{
    public int ordersPlaced;
    public int ordersDelivered;
    public float onTimeDeliveryRate;   // 0-100
    public float avgPartQuality;       // 0-100 (avg durability of delivered parts)
    public int activeDeals;
}

// ============================================================================
// KPI DRILL-DOWN DETAIL
// One reusable, data-driven payload behind every General Report card. Built on
// the server by KPIManager and fetched on demand (request/response RPC) when a
// detail panel is opened. JsonUtility-serializable like the report structs above.
// ============================================================================

// Identifies which General Report KPI a detail payload belongs to.
// Order matches the General Report cards; values are sent over the wire as ints.
public enum KpiMetric
{
    // General report
    CustomerSatisfaction,
    OnTimePerformance,
    AvgWaitingTime,
    Transfers,
    TotalBreakdowns,
    FleetReliability,
    FleetUtilization,
    CashBalance,

    // Finance report (Cash Balance reuses the existing finance dashboard, no metric needed)
    FinanceTotalRevenue,
    FinanceTotalExpenses,
    FinancePayrollSpend,
    FinancePartsSpend,
    FinanceOrdersPlaced,
    FinanceOnTimeDeliveries,
    FinanceAvgPartQuality,

    // Transportation (Operations) report
    OpsPassengersServed,
    OpsPassengersMissed,
    OpsAvailableBuses,
    OpsStopCoverage,
    OpsLongestRoute,
    OpsStopsNotCovered,

    // HR report
    HrTotalEmployees,
    HrTotalHires,
    HrAvgSkill,
    HrWeeklyPayroll,
    HrTeamCount,
    HrAvgFatigue,

    // Maintenance report
    MaintMttr,
    MaintBreakdownFrequency,
    MaintRepairCompletionRate,
    MaintSparePartDelays,
    MaintBusReturnToService,
    MaintTechnicianUtilization
}

[Serializable]
public struct KpiDetailEntry
{
    public string label;      // "Bus 03 — Engine failure" / "Passengers gave up at Stop 12"
    public float value;       // signed magnitude (e.g. -0.6, +2.5); 0 if not applicable
    public int day;           // sim day
    public float timeOfDay;   // sim hour 0–24
    public int kind;          // 0 = neutral, 1 = positive, 2 = negative (drives colour/sign)
}

[Serializable]
public struct KpiDetailData
{
    public int metric;                    // (int)KpiMetric
    public string headerValue;            // formatted current value e.g. "%80"
    public string explanation;            // one-line "how it's calculated"
    public List<KpiDetailEntry> entries;  // newest-first, capped
}