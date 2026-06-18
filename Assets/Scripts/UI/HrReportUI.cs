using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Role-specific report panel for the HR Manager (HrReport). Same pattern as OperationsReportUI.
/// avgFatigue / inTraining have no simulation source yet and render as "N/A".
/// </summary>
public class HrReportUI : MonoBehaviour
{
    private const PlayerRole RequiredRole = PlayerRole.HRManager;
    private const SyncDataType ReportType = SyncDataType.HrReport;

    [Header("Gating")]
    [Tooltip("Visible panel shown only to the matching role. Leave empty to never hide.")]
    [SerializeField] private GameObject contentRoot;

    [Header("KPI Texts")]
    [SerializeField] private TextMeshProUGUI totalEmployeesText;
    [SerializeField] private TextMeshProUGUI totalHiresText;
    [SerializeField] private TextMeshProUGUI avgSkillText;
    [SerializeField] private TextMeshProUGUI weeklyPayrollText;
    [SerializeField] private TextMeshProUGUI teamCountText;
    [SerializeField] private TextMeshProUGUI avgFatigueText;
    [SerializeField] private TextMeshProUGUI inTrainingText;

    [Header("Drill-Down")]
    [Tooltip("Shared list panel (same one the other reports use).")]
    [SerializeField] private KpiDetailPanelUI detailPanel;
    [Tooltip("One Button per card in grid order: Employees, Hires, Avg Skill, Weekly Payroll, Team Count, Avg Fatigue.")]
    [SerializeField] private Button[] cardButtons = new Button[6];

    private static readonly KpiMetric[] CardMetrics =
    {
        KpiMetric.HrTotalEmployees,  // 0
        KpiMetric.HrTotalHires,      // 1
        KpiMetric.HrAvgSkill,        // 2
        KpiMetric.HrWeeklyPayroll,   // 3
        KpiMetric.HrTeamCount,       // 4
        KpiMetric.HrAvgFatigue       // 5
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
        var d = KPIManager.Instance.GetHrReport();

        if (totalEmployeesText != null) totalEmployeesText.text = $"{d.totalEmployees}";
        if (totalHiresText != null) totalHiresText.text = $"{d.totalHires}";
        if (avgSkillText != null) avgSkillText.text = ReportFormat.Score(d.avgSkill);
        if (weeklyPayrollText != null) weeklyPayrollText.text = ReportFormat.Money(d.weeklyPayroll);
        if (teamCountText != null) teamCountText.text = $"{d.teamCount}";
        if (avgFatigueText != null) avgFatigueText.text = ReportFormat.Score(d.avgFatigue);
        if (inTrainingText != null) inTrainingText.text = ReportFormat.Count(d.inTraining);
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

    /// <summary>Opens the shared detail panel for an HR card (index matches CardMetrics).</summary>
    public void OpenCard(int index)
    {
        if (detailPanel != null && index >= 0 && index < CardMetrics.Length)
            detailPanel.Show(CardMetrics[index]);
    }
}
