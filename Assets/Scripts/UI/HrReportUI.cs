using UnityEngine;
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
        var d = KPIManager.Instance.GetHrReport();

        if (totalEmployeesText != null) totalEmployeesText.text = $"{d.totalEmployees}";
        if (totalHiresText != null) totalHiresText.text = $"{d.totalHires}";
        if (avgSkillText != null) avgSkillText.text = ReportFormat.Score(d.avgSkill);
        if (weeklyPayrollText != null) weeklyPayrollText.text = ReportFormat.Money(d.weeklyPayroll);
        if (teamCountText != null) teamCountText.text = $"{d.teamCount}";
        if (avgFatigueText != null) avgFatigueText.text = ReportFormat.Score(d.avgFatigue);
        if (inTrainingText != null) inTrainingText.text = ReportFormat.Count(d.inTraining);
    }
}
