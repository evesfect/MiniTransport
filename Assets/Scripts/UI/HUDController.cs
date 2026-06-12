using TMPro;
using UnityEngine;

/// <summary>
/// Canvas/uGUI HUD that surfaces the live company state to the player:
/// fleet size, cash balance, in-game time and day, plus a simulation
/// speed cluster (Pause / 1x / 3x / 10x).
///
/// This is the Canvas-based replacement for the legacy UIToolkit
/// GameHUDController. Wire the serialized fields in the Inspector to
/// TextMeshPro labels and AnimatedToggleButtons laid out in the same
/// style as the BottomBar prefab.
///
/// Values are polled in Update with null-checks because the data sources
/// (SimulationTimeManager / CompanyManager / FleetManager) are
/// NetworkBehaviours that spawn after this component is enabled.
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("Info Labels")]
    [Tooltip("Shows the number of buses in the fleet.")]
    [SerializeField] private TMP_Text busCountText;
    [Tooltip("Shows the company cash balance.")]
    [SerializeField] private TMP_Text moneyText;
    [Tooltip("Shows the current in-game time (HH:MM).")]
    [SerializeField] private TMP_Text timeText;
    [Tooltip("Shows the current simulation day.")]
    [SerializeField] private TMP_Text dayText;
    [Tooltip("Shows the most recent expense recorded in the ledger.")]
    [SerializeField] private TMP_Text latestExpenseText;
    [Tooltip("Shows the current customer satisfaction percentage.")]
    [SerializeField] private TMP_Text satisfactionText;

    [Header("Speed Controls (radio group)")]
    [SerializeField] private AnimatedToggleButton pauseButton;   // 0x
    [SerializeField] private AnimatedToggleButton speed1xButton;  // 1x
    [SerializeField] private AnimatedToggleButton speed2xButton;  // 2x
    [SerializeField] private AnimatedToggleButton speed5xButton;  // 5x
    [SerializeField] private AnimatedToggleButton speed10xButton; // 10x

    [Header("Formatting")]
    [Tooltip("Prepended to the formatted balance, e.g. \"$\".")]
    [SerializeField] private string moneyPrefix = "$";
    [Tooltip("{0} is replaced by the bus count, e.g. \"{0} Buses\".")]
    [SerializeField] private string busCountFormat = "{0}";
    [Tooltip("{0} is replaced by the day number, e.g. \"Day {0}\".")]
    [SerializeField] private string dayFormat = "Day {0}";
    [Tooltip("{0} = expense description, {1} = formatted amount (with money prefix).")]
    [SerializeField] private string latestExpenseFormat = "Last Expense: {0}  -{1}";
    [Tooltip("Shown when no expense has been recorded yet.")]
    [SerializeField] private string noExpenseText = "Last Expense: —";
    [Tooltip("{0} = satisfaction percentage.")]
    [SerializeField] private string satisfactionFormat = "Satisfaction: {0}%";

    // -1 forces the first sync to push the visual state regardless of value.
    private float _lastSyncedMultiplier = -1f;

    // The cash balance only reaches a non-host client if this client has registered interest in
    // CompanyStats with the data broker (which drives the server-side subscription). The HUD is
    // always on, so it holds a permanent interest. Tracked so we register exactly once and release it.
    private bool _companyInterestRegistered = false;

    private void OnEnable()
    {
        WireSpeedButton(pauseButton, 0f);
        WireSpeedButton(speed1xButton, 1f);
        WireSpeedButton(speed2xButton, 2f);
        WireSpeedButton(speed5xButton, 5f);
        WireSpeedButton(speed10xButton, 10f);

        // Force a re-sync next frame so the buttons reflect the real multiplier.
        _lastSyncedMultiplier = -1f;
    }

    private void OnDisable()
    {
        UnwireSpeedButton(pauseButton);
        UnwireSpeedButton(speed1xButton);
        UnwireSpeedButton(speed2xButton);
        UnwireSpeedButton(speed5xButton);
        UnwireSpeedButton(speed10xButton);

        ReleaseCompanyInterest();
    }

    private void Update()
    {
        EnsureCompanyInterest();

        var time = SimulationTimeManager.Instance;
        if (time != null)
        {
            if (timeText != null) timeText.text = time.GetTimeString(time.VisualTime);
            if (dayText != null) dayText.text = string.Format(dayFormat, time.CurrentDay);
            SyncSpeedButtons(time.TimeMultiplier);
        }

        var company = CompanyManager.Instance;
        if (moneyText != null && company != null)
        {
            var data = company.GetCompanyData();
            if (data != null)
                moneyText.text = moneyPrefix + ReportFormat.Money(data.CurrentBalance);
        }

        if (latestExpenseText != null && company != null)
        {
            if (company.HasLatestExpense)
            {
                string amount = moneyPrefix + ReportFormat.Money(Mathf.Abs(company.LatestExpenseAmount));
                latestExpenseText.text = string.Format(latestExpenseFormat, company.LatestExpenseDescription, amount);
            }
            else
            {
                latestExpenseText.text = noExpenseText;
            }
        }

        if (satisfactionText != null && company != null)
            satisfactionText.text = string.Format(satisfactionFormat, Mathf.RoundToInt(company.Satisfaction));

        if (busCountText != null && FleetManager.Instance != null)
        {
            busCountText.text = string.Format(busCountFormat, FleetManager.Instance.ActiveBusCount);
        }
    }

    // --- Data Subscription ---

    // Registered from Update (not OnEnable) because LocalDataBroker may not exist yet when the HUD
    // is first enabled. The early registration is harmless and gets re-sent on client connect.
    private void EnsureCompanyInterest()
    {
        if (_companyInterestRegistered || LocalDataBroker.Instance == null) return;
        LocalDataBroker.Instance.RegisterInterest(SyncDataType.CompanyStats);
        _companyInterestRegistered = true;
    }

    private void ReleaseCompanyInterest()
    {
        if (!_companyInterestRegistered || LocalDataBroker.Instance == null) return;
        LocalDataBroker.Instance.UnregisterInterest(SyncDataType.CompanyStats);
        _companyInterestRegistered = false;
    }

    // --- Speed Control Logic ---

    private void WireSpeedButton(AnimatedToggleButton button, float multiplier)
    {
        if (button == null) return;
        // Runtime listeners are cleared in OnDisable, so each enable wires fresh.
        button.onValueChanged.AddListener(isOn => OnSpeedToggled(button, multiplier, isOn));
    }

    private void UnwireSpeedButton(AnimatedToggleButton button)
    {
        if (button == null) return;
        button.onValueChanged.RemoveAllListeners();
    }

    private void OnSpeedToggled(AnimatedToggleButton source, float multiplier, bool isOn)
    {
        if (isOn)
        {
            // Radio behaviour: turn the siblings off without re-notifying.
            SetSiblingsOff(source);
            RequestMultiplier(multiplier);
        }
        else
        {
            // A radio group can't have zero selected: if the user clicked the
            // already-active option, snap it back on without firing again.
            source.SetState(true, notify: false);
        }
    }

    private void SetSiblingsOff(AnimatedToggleButton active)
    {
        SetOffIfNot(pauseButton, active);
        SetOffIfNot(speed1xButton, active);
        SetOffIfNot(speed2xButton, active);
        SetOffIfNot(speed5xButton, active);
        SetOffIfNot(speed10xButton, active);
    }

    private void SetOffIfNot(AnimatedToggleButton button, AnimatedToggleButton active)
    {
        if (button != null && button != active && button.isOn)
            button.SetState(false, notify: false);
    }

    private void RequestMultiplier(float multiplier)
    {
        SimulationTimeManager.Instance?.RequestTimeMultiplierRpc(multiplier);
    }

    /// <summary>
    /// Reflects the authoritative network multiplier on the buttons (handles
    /// initial state and changes driven by other clients / the server).
    /// Uses notify:false so syncing never re-issues an RPC.
    /// </summary>
    private void SyncSpeedButtons(float multiplier)
    {
        if (Mathf.Approximately(multiplier, _lastSyncedMultiplier)) return;
        _lastSyncedMultiplier = multiplier;

        SetButtonState(pauseButton, Mathf.Approximately(multiplier, 0f));
        SetButtonState(speed1xButton, Mathf.Approximately(multiplier, 1f));
        SetButtonState(speed2xButton, Mathf.Approximately(multiplier, 2f));
        SetButtonState(speed5xButton, Mathf.Approximately(multiplier, 5f));
        SetButtonState(speed10xButton, Mathf.Approximately(multiplier, 10f));
    }

    private void SetButtonState(AnimatedToggleButton button, bool shouldBeOn)
    {
        if (button != null && button.isOn != shouldBeOn)
            button.SetState(shouldBeOn, notify: false);
    }
}
