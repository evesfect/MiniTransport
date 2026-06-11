using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Role-specific report panel for the Transport Manager (OperationsReport). Mirrors the
/// GeneralReportUI pattern: reads live KPI data from KPIManager and, on clients, asks
/// NetworkSyncBroker to stream this report only while the panel is shown. Self-gates via
/// RoleManager so only the matching role sees it (assign <see cref="contentRoot"/> to the
/// visible panel child; keep this component on an always-active parent).
/// </summary>
public class OperationsReportUI : MonoBehaviour
{
    private const PlayerRole RequiredRole = PlayerRole.TransportManager;
    private const SyncDataType ReportType = SyncDataType.OperationsReport;

    [Header("Gating")]
    [Tooltip("Visible panel shown only to the matching role. Leave empty to never hide.")]
    [SerializeField] private GameObject contentRoot;

    [Header("KPI Texts")]
    [SerializeField] private TextMeshProUGUI onTimePerformanceText;
    [SerializeField] private TextMeshProUGUI avgWaitingTimeText;
    [SerializeField] private TextMeshProUGUI transfersText;
    [SerializeField] private TextMeshProUGUI fleetUtilizationText;
    [SerializeField] private TextMeshProUGUI passengersServedText;
    [SerializeField] private TextMeshProUGUI passengersMissedText;
    [SerializeField] private TextMeshProUGUI availableBusesText;
    [SerializeField] private TextMeshProUGUI stopCoverageText;
    [SerializeField] private TextMeshProUGUI longestRouteText;       // highest stop count on a route
    [SerializeField] private TextMeshProUGUI stopsNotCoveredText;    // stops served by no route

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

    // Shows this panel and streams its report only to the matching role.
    private void ApplyGate()
    {
        bool mine = RoleManager.Instance != null && RoleManager.Instance.GetMyRole() == RequiredRole;

        if (contentRoot != null) contentRoot.SetActive(mine);
        SetReportSubscription(mine);
        if (mine) Render();
    }

    // Clients subscribe so the server only streams this report while it is shown; host reads locally.
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
        var d = KPIManager.Instance.GetOperationsReport();

        if (onTimePerformanceText != null) onTimePerformanceText.text = ReportFormat.Pct(d.onTimePerformance);
        if (avgWaitingTimeText != null) avgWaitingTimeText.text = ReportFormat.Minutes(d.avgWaitMinutes);
        if (transfersText != null) transfersText.text = $"{d.transfers}";
        if (fleetUtilizationText != null) fleetUtilizationText.text = ReportFormat.Pct(d.fleetUtilization);
        if (passengersServedText != null) passengersServedText.text = $"{d.passengersServed}";
        if (passengersMissedText != null) passengersMissedText.text = $"{d.passengersMissed}";
        if (availableBusesText != null) availableBusesText.text = $"{d.availableBuses}";
        if (stopCoverageText != null) stopCoverageText.text = ReportFormat.Pct(d.stopCoverage);
        if (longestRouteText != null) longestRouteText.text = $"{d.longestRouteStops} stops";
        if (stopsNotCoveredText != null) stopsNotCoveredText.text = $"{d.stopsNotCovered}";
    }
}
