using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Role-specific report panel for the Finance Manager (FinanceReport). Same pattern as
/// OperationsReportUI. Procurement/vendor KPIs (orders placed, on-time delivery, part quality)
/// are part of this report since orders are the Finance Manager's responsibility.
/// </summary>
public class FinanceReportUI : MonoBehaviour
{
    private const PlayerRole RequiredRole = PlayerRole.FinanceManager;
    private const SyncDataType ReportType = SyncDataType.FinanceReport;

    [Header("Gating")]
    [Tooltip("Visible panel shown only to the matching role. Leave empty to never hide.")]
    [SerializeField] private GameObject contentRoot;

    [Header("KPI Texts")]
    [SerializeField] private TextMeshProUGUI cashBalanceText;
    [SerializeField] private TextMeshProUGUI totalRevenueText;
    [SerializeField] private TextMeshProUGUI totalExpensesText;
    [SerializeField] private TextMeshProUGUI payrollSpendText;
    [SerializeField] private TextMeshProUGUI partsSpendText;

    [Header("Procurement / Vendor KPIs")]
    [SerializeField] private TextMeshProUGUI ordersPlacedText;
    [SerializeField] private TextMeshProUGUI onTimeDeliveryRateText;
    [SerializeField] private TextMeshProUGUI avgPartQualityText;

    private bool _subscribed;

    private void OnEnable()
    {
        if (KPIManager.Instance != null)
            KPIManager.Instance.OnReportsUpdated += Render;
        if (RoleManager.Instance != null)
            RoleManager.Instance.OnRolesUpdated += ApplyGate;

        ApplyGate();
    }

    private void OnDisable()
    {
        if (KPIManager.Instance != null)
            KPIManager.Instance.OnReportsUpdated -= Render;
        if (RoleManager.Instance != null)
            RoleManager.Instance.OnRolesUpdated -= ApplyGate;

        SetReportSubscription(false);
    }

    private void ApplyGate()
    {
        bool mine = RoleManager.Instance != null && RoleManager.Instance.GetMyRole() == RequiredRole;

        if (contentRoot != null) contentRoot.SetActive(mine);
        SetReportSubscription(mine);
        if (mine) Render();
    }

    private void SetReportSubscription(bool on)
    {
        if (on == _subscribed) return;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer || NetworkSyncBroker.Instance == null)
        {
            _subscribed = on;
            return;
        }

        if (on) NetworkSyncBroker.Instance.SubscribeRpc(ReportType);
        else NetworkSyncBroker.Instance.UnsubscribeRpc(ReportType);
        _subscribed = on;
    }

    private void Render()
    {
        if (KPIManager.Instance == null) return;
        var d = KPIManager.Instance.GetFinanceReport();

        if (cashBalanceText != null) cashBalanceText.text = ReportFormat.Money(d.cashBalance);
        if (totalRevenueText != null) totalRevenueText.text = ReportFormat.Money(d.totalRevenue);
        if (totalExpensesText != null) totalExpensesText.text = ReportFormat.Money(d.totalExpenses);
        if (payrollSpendText != null) payrollSpendText.text = ReportFormat.Money(d.payrollSpend);
        if (partsSpendText != null) partsSpendText.text = ReportFormat.Money(d.partsSpend);
        if (ordersPlacedText != null) ordersPlacedText.text = $"{d.ordersPlaced}";
        if (onTimeDeliveryRateText != null) onTimeDeliveryRateText.text = ReportFormat.Pct(d.onTimeDeliveryRate);
        if (avgPartQualityText != null) avgPartQualityText.text = ReportFormat.Score(d.avgPartQuality);
    }
}
