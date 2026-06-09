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
    public float stopCoverage;         // STUB (-1 = not tracked): % of stops served by >=1 route
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
    public float mttrHours;            // STUB (-1): Mean Time To Repair (breakdown -> return-to-service)
    public float technicianUtilization;// STUB (-1): crew workload vs capacity
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
    public float maintenanceSpend;
    public float payrollSpend;
    public float partsSpend;
    public float weeklyNetProfit;      // revenue - expenses over the last 7 days
    public float weeklyCashBurn;       // expenses over the last 7 days
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