using UnityEngine;
using UnityEngine.UIElements;

public class TimerHUDController : MonoBehaviour
{
    private UIDocument _doc;
    
    // UI Elements
    private Label _dateLabel;
    private Label _timeLabel;
    private Button _btnPause; // New Button
    private Button _btn1x;
    private Button _btn3x;
    private Button _btn10x;

    private const string SelectedClassName = "selected";

    private void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        if (_doc == null)
        {
            Debug.LogError("TimerHUDController: No UIDocument found!");
            return;
        }

        var root = _doc.rootVisualElement;

        // Query Elements
        _dateLabel = root.Q<Label>("DateLabel");
        _timeLabel = root.Q<Label>("TimeLabel");
        
        _btnPause = root.Q<Button>("BtnPause"); // Query new button
        _btn1x = root.Q<Button>("Btn1x");
        _btn3x = root.Q<Button>("Btn3x");
        _btn10x = root.Q<Button>("Btn10x");

        // Register Click Events
        if (_btnPause != null) _btnPause.clicked += () => SetMultiplier(0f); // 0 = Pause
        if (_btn1x != null) _btn1x.clicked += () => SetMultiplier(1f);
        if (_btn3x != null) _btn3x.clicked += () => SetMultiplier(3f);
        if (_btn10x != null) _btn10x.clicked += () => SetMultiplier(10f);
    }

    private void Update()
    {
        if (SimulationTimeManager.Instance == null) return;
        if (_dateLabel == null || _timeLabel == null) return;

        // 1. Update Date
        int currentDay = SimulationTimeManager.Instance.CurrentDay;
        _dateLabel.text = $"Day {currentDay}";

        // 2. Update Time
        float time = SimulationTimeManager.Instance.VisualTime;
        _timeLabel.text = SimulationTimeManager.Instance.GetTimeString(time);

        // 3. Update Button Visual State
        UpdateSpeedButtons(SimulationTimeManager.Instance.TimeMultiplier);
    }

    private void SetMultiplier(float mult)
    {
        if (SimulationTimeManager.Instance != null)
        {
            SimulationTimeManager.Instance.RequestTimeMultiplierRpc(mult);
        }
    }

    private void UpdateSpeedButtons(float currentMultiplier)
    {
        if (_btnPause == null || _btn1x == null || _btn3x == null || _btn10x == null) return;

        // Reset all buttons
        _btnPause.RemoveFromClassList(SelectedClassName);
        _btn1x.RemoveFromClassList(SelectedClassName);
        _btn3x.RemoveFromClassList(SelectedClassName);
        _btn10x.RemoveFromClassList(SelectedClassName);

        // Highlight the correct one
        if (Mathf.Approximately(currentMultiplier, 0f))
        {
            _btnPause.AddToClassList(SelectedClassName);
        }
        else if (Mathf.Approximately(currentMultiplier, 1f))
        {
            _btn1x.AddToClassList(SelectedClassName);
        }
        else if (Mathf.Approximately(currentMultiplier, 3f))
        {
            _btn3x.AddToClassList(SelectedClassName);
        }
        else if (Mathf.Approximately(currentMultiplier, 10f))
        {
            _btn10x.AddToClassList(SelectedClassName);
        }
    }
}