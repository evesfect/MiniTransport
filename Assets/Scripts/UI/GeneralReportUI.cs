using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Binds the shared General Report panel to live KPI data, reading directly from KPIManager
/// (no ClientDataCache / LocalDataBroker needed). On open it subscribes to NetworkSyncBroker so
/// the server only streams report data to clients who actually have the panel open.
/// Wire the TMP fields (currently hardcoded "%80" / "457" / ... in the scene) in the Inspector.
/// </summary>
public class GeneralReportUI : MonoBehaviour
{
    [Header("KPI Texts")]
    [SerializeField] private TextMeshProUGUI customerSatisfactionText;
    [SerializeField] private TextMeshProUGUI onTimePerformanceText;
    [SerializeField] private TextMeshProUGUI avgWaitingTimeText;
    [SerializeField] private TextMeshProUGUI transfersText;
    [SerializeField] private TextMeshProUGUI totalBreakdownsText;
    [SerializeField] private TextMeshProUGUI fleetReliabilityText;
    [SerializeField] private TextMeshProUGUI fleetUtilizationText;
    [SerializeField] private TextMeshProUGUI cashBalanceText;

    [Header("Drill-Down")]
    [Tooltip("Shared detail panel opened when a KPI card is clicked.")]
    [SerializeField] private KpiDetailPanelUI detailPanel;
    [Tooltip("One Button per card, in the same order as KpiMetric (Satisfaction, OnTime, Wait, Transfers, Breakdowns, Reliability, Utilization, Cash).")]
    [SerializeField] private Button[] cardButtons = new Button[8];

    private void OnEnable()
    {
        if (KPIManager.Instance != null)
            KPIManager.Instance.OnReportsUpdated += Render;

        WireCardButtons();

        // Clients ask the server to start streaming this report (host reads locally).
        // IsListening guards against firing an RPC before the NetworkManager has started.
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.IsServer && NetworkSyncBroker.Instance != null)
            NetworkSyncBroker.Instance.SubscribeRpc(SyncDataType.GeneralReport);

        Render();
    }

    private void OnDisable()
    {
        if (KPIManager.Instance != null)
            KPIManager.Instance.OnReportsUpdated -= Render;

        UnwireCardButtons();

        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.IsServer && NetworkSyncBroker.Instance != null)
            NetworkSyncBroker.Instance.UnsubscribeRpc(SyncDataType.GeneralReport);
    }

    private void Render()
    {
        if (KPIManager.Instance == null) return;
        var d = KPIManager.Instance.GetGeneralReport();

        if (customerSatisfactionText != null) customerSatisfactionText.text = $"%{d.customerSatisfaction:F0}";
        if (onTimePerformanceText != null) onTimePerformanceText.text = $"%{d.onTimePerformance:F0}";
        if (avgWaitingTimeText != null) avgWaitingTimeText.text = $"{d.avgWaitMinutes:F0} min";
        if (transfersText != null) transfersText.text = $"{d.transfers}";
        if (totalBreakdownsText != null) totalBreakdownsText.text = $"{d.totalBreakdowns}";
        if (fleetReliabilityText != null) fleetReliabilityText.text = $"%{d.fleetReliability:F0}";
        if (fleetUtilizationText != null) fleetUtilizationText.text = $"%{d.fleetUtilization:F0}";
        if (cashBalanceText != null) cashBalanceText.text = $"{d.cashBalance:N0}";
    }

    // --- Drill-down ---

    private void WireCardButtons()
    {
        if (cardButtons == null) return;
        for (int i = 0; i < cardButtons.Length; i++)
        {
            if (cardButtons[i] == null) continue;
            int metric = i; // capture for the closure
            cardButtons[i].onClick.AddListener(() => OpenKpiDetail(metric));
        }
    }

    private void UnwireCardButtons()
    {
        if (cardButtons == null) return;
        foreach (var btn in cardButtons)
            if (btn != null) btn.onClick.RemoveAllListeners();
    }

    /// <summary>Opens the shared detail panel for the given KPI (index matches KpiMetric).
    /// Public so it can also be wired directly from a Button's Inspector OnClick if preferred.</summary>
    public void OpenKpiDetail(int metric)
    {
        if (detailPanel == null) return;
        detailPanel.Show((KpiMetric)metric);
    }
}
