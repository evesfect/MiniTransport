using System.Text;
using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Per-player end-of-game report. Resolves the local player's assigned role from
/// PlayerRoleManager and shows only that role's domain KPIs, reading directly from KPIManager
/// (no ClientDataCache / LocalDataBroker needed). Only the local role's report is subscribed to,
/// so a client receives no data for the other domains.
///
/// Renders into a single TMP block to avoid wiring a field per metric across five layouts.
/// Set useFixedRole to pin a panel to one domain (e.g. a standalone Maintenance Report).
/// </summary>
public class RoleReportUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;

    [Header("Role Source")]
    [Tooltip("If true, always show 'fixedRole' instead of following the local player's assigned role. " +
             "Use for a panel pinned to one domain (e.g. the scene's standalone Maintenance Report).")]
    [SerializeField] private bool useFixedRole = false;
    [SerializeField] private PlayerRole fixedRole = PlayerRole.Maintenance;

    private SyncDataType _activeType = SyncDataType.None;

    private void OnEnable()
    {
        if (KPIManager.Instance != null)
            KPIManager.Instance.OnReportsUpdated += Render;

        if (PlayerRoleManager.Instance != null)
            PlayerRoleManager.Instance.OnRolesChanged += ResolveRole;

        ResolveRole();
    }

    private void OnDisable()
    {
        if (KPIManager.Instance != null)
            KPIManager.Instance.OnReportsUpdated -= Render;

        if (PlayerRoleManager.Instance != null)
            PlayerRoleManager.Instance.OnRolesChanged -= ResolveRole;

        Unsubscribe(_activeType);
        _activeType = SyncDataType.None;
    }

    // Role may sync slightly after the panel opens, so this can be called again to switch.
    private void ResolveRole()
    {
        PlayerRole role;
        if (useFixedRole)
            role = fixedRole;
        else
            role = PlayerRoleManager.Instance != null
                ? PlayerRoleManager.Instance.GetLocalPlayerRole()
                : PlayerRole.Operations;

        SyncDataType newType = PlayerRoleManager.RoleToReport(role);
        if (newType == _activeType) { Render(); return; }

        Unsubscribe(_activeType);
        Subscribe(newType);

        _activeType = newType;
        if (titleText != null) titleText.text = $"{role} Report";
        Render();
    }

    // Clients ask the server to stream a given report; the host reads locally.
    private void Subscribe(SyncDataType type)
    {
        if (type == SyncDataType.None) return;
        // IsListening guards against firing an RPC before the NetworkManager has started.
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.IsServer && NetworkSyncBroker.Instance != null)
            NetworkSyncBroker.Instance.SubscribeRpc(type);
    }

    private void Unsubscribe(SyncDataType type)
    {
        if (type == SyncDataType.None) return;
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.IsServer && NetworkSyncBroker.Instance != null)
            NetworkSyncBroker.Instance.UnsubscribeRpc(type);
    }

    private void Render()
    {
        if (KPIManager.Instance == null || bodyText == null) return;

        switch (_activeType)
        {
            case SyncDataType.OperationsReport: bodyText.text = Operations(KPIManager.Instance.GetOperationsReport()); break;
            case SyncDataType.MaintenanceReport: bodyText.text = Maintenance(KPIManager.Instance.GetMaintenanceReport()); break;
            case SyncDataType.HrReport: bodyText.text = Hr(KPIManager.Instance.GetHrReport()); break;
            case SyncDataType.FinanceReport: bodyText.text = Finance(KPIManager.Instance.GetFinanceReport()); break;
            case SyncDataType.VendorReport: bodyText.text = Vendor(KPIManager.Instance.GetVendorReport()); break;
        }
    }

    // Stub fields are encoded as a negative value; show "N/A" until the system exists.
    private static string NA(float v, string suffix = "") => v < 0f ? "N/A" : $"{v:F0}{suffix}";
    private static string NA(int v) => v < 0 ? "N/A" : $"{v}";

    private static string Operations(OperationsReportData d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"On-Time Performance: %{d.onTimePerformance:F0}");
        sb.AppendLine($"Average Waiting Time: {d.avgWaitMinutes:F0} min");
        sb.AppendLine($"Fleet Utilization: %{d.fleetUtilization:F0}");
        sb.AppendLine($"Available Buses: {d.availableBuses}");
        sb.AppendLine($"Stop Coverage: {NA(d.stopCoverage, "%")}");
        sb.AppendLine($"Passengers Served: {d.passengersServed}");
        sb.AppendLine($"Passengers Missed: {d.passengersMissed}");
        sb.AppendLine($"Transfers: {d.transfers}");
        return sb.ToString();
    }

    private static string Maintenance(MaintenanceReportData d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Total Breakdowns: {d.totalBreakdowns}");
        sb.AppendLine($"Repairs Completed: {d.repairsCompleted}");
        sb.AppendLine($"Parts Replaced: {d.partsReplaced}");
        sb.AppendLine($"MTTR: {NA(d.mttrHours, " hrs")}");
        sb.AppendLine($"Technician Utilization: {NA(d.technicianUtilization, "%")}");
        sb.AppendLine($"Fleet Reliability: %{d.fleetReliability:F0}");
        sb.AppendLine($"Avg Fleet Health: %{d.avgFleetHealth:F0}");
        sb.AppendLine($"Available Buses: {d.availableBuses}");
        sb.AppendLine($"Buses Needing Repair: {d.busesNeedingRepair}");
        return sb.ToString();
    }

    private static string Hr(HrReportData d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Total Employees: {d.totalEmployees}");
        sb.AppendLine($"Total Hires: {d.totalHires}");
        sb.AppendLine($"Average Skill: {d.avgSkill:F0}");
        sb.AppendLine($"Avg Fatigue: {NA(d.avgFatigue)}");
        sb.AppendLine($"In Training: {NA(d.inTraining)}");
        sb.AppendLine($"Weekly Payroll: {d.weeklyPayroll:N0}");
        sb.AppendLine($"Teams: {d.teamCount}");
        return sb.ToString();
    }

    private static string Finance(FinanceReportData d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Cash Balance: {d.cashBalance:N0}");
        sb.AppendLine($"Weekly Net Profit: {d.weeklyNetProfit:N0}");
        sb.AppendLine($"Weekly Cash Burn: {d.weeklyCashBurn:N0}");
        sb.AppendLine($"Total Revenue: {d.totalRevenue:N0}");
        sb.AppendLine($"Total Expenses: {d.totalExpenses:N0}");
        sb.AppendLine($"Maintenance Spend: {d.maintenanceSpend:N0}");
        sb.AppendLine($"Payroll Spend: {d.payrollSpend:N0}");
        sb.AppendLine($"Parts Spend: {d.partsSpend:N0}");
        return sb.ToString();
    }

    private static string Vendor(VendorReportData d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Orders Placed: {d.ordersPlaced}");
        sb.AppendLine($"Orders Delivered: {d.ordersDelivered}");
        sb.AppendLine($"On-Time Delivery: %{d.onTimeDeliveryRate:F0}");
        sb.AppendLine($"Avg Part Quality: %{d.avgPartQuality:F0}");
        sb.AppendLine($"Active Deals: {d.activeDeals}");
        return sb.ToString();
    }
}
