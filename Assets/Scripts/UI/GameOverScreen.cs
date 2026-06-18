using UnityEngine;
using TMPro;

/// <summary>
/// Client-side game-over overlay. Watches GameEndManager; when the game ends it shows the overlay
/// with a reason banner and reveals the report panels. The role report UIs self-gate via
/// RoleManager, so each player automatically sees the global report plus their own role report.
/// Wire <see cref="overlayRoot"/>, <see cref="reasonText"/> and <see cref="reportsRoot"/> in the Inspector.
/// </summary>
public class GameOverScreen : MonoBehaviour
{
    [Header("Overlay")]
    [Tooltip("Root object of the game-over overlay; hidden until the game ends.")]
    [SerializeField] private GameObject overlayRoot;
    [SerializeField] private TextMeshProUGUI reasonText;
    [Tooltip("Every KPI report dashboard GameObject to reveal at game over (global + each role " +
             "panel). Each panel decides for itself whether to display, via its own role gating.")]
    [SerializeField] private GameObject[] reportPanels;

    private bool _subscribed;
    private bool _shown;

    private void OnEnable()
    {
        // Only pre-hide before the game is over. This component can live under overlayRoot, so once
        // Show() has revealed the overlay we must NOT re-hide here — doing so fights Show() and, with
        // the re-entrant activation below, spins into an infinite OnEnable -> Show -> OnEnable loop.
        if (!_shown && overlayRoot != null) overlayRoot.SetActive(false);
        TrySubscribe();
    }

    private void Update()
    {
        // GameEndManager spawns with the network; keep trying until it exists.
        if (!_subscribed) TrySubscribe();
    }

    private void OnDisable()
    {
        if (_subscribed && GameEndManager.Instance != null)
            GameEndManager.Instance.OnGameEnded -= Show;
        _subscribed = false;
    }

    private void TrySubscribe()
    {
        if (_subscribed || GameEndManager.Instance == null) return;

        GameEndManager.Instance.OnGameEnded += Show;
        _subscribed = true;

        // Already over (e.g. late join): reflect immediately.
        if (GameEndManager.Instance.IsGameOver) Show(GameEndManager.Instance.Reason);
    }

    private void Show(GameEndReason reason)
    {
        // Idempotent. Activating overlayRoot / the report panels can re-enable this component (it
        // may sit under overlayRoot), which re-runs OnEnable -> TrySubscribe -> Show. Guard against
        // that re-entrancy so the UI is revealed exactly once instead of recursing forever.
        if (_shown) return;
        _shown = true;

        if (overlayRoot != null) overlayRoot.SetActive(true);
        if (reasonText != null) reasonText.text = ReasonLabel(reason);

        // Reveal every report dashboard; each one's own role gating shows it only to the
        // matching role (the global report is ungated, so it shows for everyone).
        if (reportPanels != null)
        {
            foreach (var panel in reportPanels)
            {
                if (panel == null) { Debug.LogWarning("[GameOverScreen] A Report Panels slot is empty."); continue; }
                RevealPanel(panel);
            }
        }
    }

    // A panel can be revealed one of two ways depending on how it manages its own visibility:
    //   * BasePanel-managed panels (e.g. the General report, which the bottom-bar button also
    //     toggles) hide via a CanvasGroup while staying active, so SetActive(true) would be a
    //     no-op and leave them invisible — they must be opened through the BasePanel/UIManager.
    //   * Plain panels simply toggle their GameObject.
    private static void RevealPanel(GameObject panel)
    {
        if (panel.TryGetComponent<BasePanel>(out var basePanel))
            basePanel.SetPanelState(true);
        else
            panel.SetActive(true);
    }

    private static string ReasonLabel(GameEndReason reason) => reason switch
    {
        GameEndReason.Bankrupt  => "BANKRUPT\nThe company ran out of money.",
        GameEndReason.TimeLimit => "TIME'S UP\nThe simulation period has ended.",
        _                       => "GAME OVER"
    };
}
