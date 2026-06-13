using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Role-specific report panel for the Maintenance Manager (MaintenanceReport). Same pattern as
/// OperationsReportUI: live KPI data from KPIManager, client-side streaming via NetworkSyncBroker
/// while shown, and RoleManager self-gating through <see cref="contentRoot"/>.
/// </summary>
public class MaintenanceReportUI : MonoBehaviour
{
    private const PlayerRole RequiredRole = PlayerRole.MaintenanceManager;
    private const SyncDataType ReportType = SyncDataType.MaintenanceReport;

    [Header("Gating")]
    [Tooltip("Visible panel shown only to the matching role. Leave empty to never hide.")]
    [SerializeField] private GameObject contentRoot;

    [Header("KPI Texts")]
    [SerializeField] private TextMeshProUGUI totalBreakdownsText;
    [SerializeField] private TextMeshProUGUI repairsCompletedText;
    [SerializeField] private TextMeshProUGUI partsReplacedText;
    [SerializeField] private TextMeshProUGUI fleetReliabilityText;
    [SerializeField] private TextMeshProUGUI avgFleetHealthText;
    [SerializeField] private TextMeshProUGUI busesNeedingRepairText;
    [SerializeField] private TextMeshProUGUI availableBusesText;
    [SerializeField] private TextMeshProUGUI mttrText;
    [SerializeField] private TextMeshProUGUI technicianUtilizationText;

    [Header("Role KPIs (per design doc)")]
    [SerializeField] private TextMeshProUGUI breakdownFrequencyText;        // breakdowns per day
    [SerializeField] private TextMeshProUGUI repairCompletionRateText;      // % of breakdowns resolved
    [SerializeField] private TextMeshProUGUI sparePartDelayFrequencyText;   // count of parts-delay stalls
    [SerializeField] private TextMeshProUGUI busReturnToServiceText;        // total downtime: sum of all repair durations

    [Header("Drill-Down")]
    [Tooltip("Shared list panel (same one the other reports use).")]
    [SerializeField] private KpiDetailPanelUI detailPanel;
    [Tooltip("One Button per card in grid order: MTTR, Breakdown Freq, Repair Completion, Spare Part Delays, Bus Return to Service, Technician Utilization.")]
    [SerializeField] private Button[] cardButtons = new Button[6];

    private static readonly KpiMetric[] CardMetrics =
    {
        KpiMetric.MaintMttr,                  // 0
        KpiMetric.MaintBreakdownFrequency,    // 1
        KpiMetric.MaintRepairCompletionRate,  // 2
        KpiMetric.MaintSparePartDelays,       // 3
        KpiMetric.MaintBusReturnToService,    // 4
        KpiMetric.MaintTechnicianUtilization  // 5
    };

    private bool _subscribed;

    private void OnEnable()
    {
        if (KPIManager.Instance != null)
            KPIManager.Instance.OnReportsUpdated += Render;
        if (RoleManager.Instance != null)
            RoleManager.Instance.OnRolesUpdated += ApplyGate;

        WireCardButtons();
        ApplyGate();
    }

    private void OnDisable()
    {
        if (KPIManager.Instance != null)
            KPIManager.Instance.OnReportsUpdated -= Render;
        if (RoleManager.Instance != null)
            RoleManager.Instance.OnRolesUpdated -= ApplyGate;

        UnwireCardButtons();
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
        var d = KPIManager.Instance.GetMaintenanceReport();

        if (totalBreakdownsText != null) totalBreakdownsText.text = $"{d.totalBreakdowns}";
        if (repairsCompletedText != null) repairsCompletedText.text = $"{d.repairsCompleted}";
        if (partsReplacedText != null) partsReplacedText.text = $"{d.partsReplaced}";
        if (fleetReliabilityText != null) fleetReliabilityText.text = ReportFormat.Pct(d.fleetReliability);
        if (avgFleetHealthText != null) avgFleetHealthText.text = ReportFormat.Pct(d.avgFleetHealth);
        if (busesNeedingRepairText != null) busesNeedingRepairText.text = $"{d.busesNeedingRepair}";
        if (availableBusesText != null) availableBusesText.text = $"{d.availableBuses}";
        if (mttrText != null) mttrText.text = ReportFormat.Hours(d.mttrHours);
        if (technicianUtilizationText != null) technicianUtilizationText.text = ReportFormat.Pct(d.technicianUtilization);

        if (breakdownFrequencyText != null) breakdownFrequencyText.text = ReportFormat.PerDay(d.breakdownFrequency);
        if (repairCompletionRateText != null) repairCompletionRateText.text = ReportFormat.Pct(d.repairCompletionRate);
        if (sparePartDelayFrequencyText != null) sparePartDelayFrequencyText.text = $"{d.sparePartDelays}";
        if (busReturnToServiceText != null) busReturnToServiceText.text = ReportFormat.Hours(d.totalDowntimeHours);
    }

    // --- Drill-down ---

    private void WireCardButtons()
    {
        if (cardButtons == null) return;
        for (int i = 0; i < cardButtons.Length; i++)
        {
            if (cardButtons[i] == null) continue;
            int index = i; // capture for closure
            cardButtons[i].onClick.AddListener(() => OpenCard(index));
        }
    }

    private void UnwireCardButtons()
    {
        if (cardButtons == null) return;
        foreach (var btn in cardButtons)
            if (btn != null) btn.onClick.RemoveAllListeners();
    }

    /// <summary>Opens the shared detail panel for a Maintenance card (index matches CardMetrics).</summary>
    public void OpenCard(int index)
    {
        if (detailPanel != null && index >= 0 && index < CardMetrics.Length)
            detailPanel.Show(CardMetrics[index]);
    }
}
