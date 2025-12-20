using UnityEngine;
using UnityEngine.UIElements;

public class GameHUDController : MonoBehaviour
{
    private UIDocument _doc;
    
    // Timer Elements
    private Label _dateLabel;
    private Label _timeLabel;
    private Button _btnPause, _btn1x, _btn3x, _btn10x;

    // Inspection Elements
    private Button _btnInspection;
    private VisualElement _dropdownPanel;
    private Button _btnToggleRoutes;
    private Button _btnToggleFleets;

    private const string SelectedClassName = "selected";
    private const string ActiveClassName = "active"; // For toggles

    private bool _isInspectionOpen = false;
    private bool _isRoutesVisible = false;
    private bool _isFleetsVisible = false;

    private void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        if (_doc == null) return;
        var root = _doc.rootVisualElement;

        // --- Query Timer Elements ---
        _dateLabel = root.Q<Label>("DateLabel");
        _timeLabel = root.Q<Label>("TimeLabel");
        _btnPause = root.Q<Button>("BtnPause");
        _btn1x = root.Q<Button>("Btn1x");
        _btn3x = root.Q<Button>("Btn3x");
        _btn10x = root.Q<Button>("Btn10x");

        // --- Query Inspection Elements ---
        _btnInspection = root.Q<Button>("BtnInspection");
        _dropdownPanel = root.Q<VisualElement>("InspectionDropdown");
        _btnToggleRoutes = root.Q<Button>("BtnToggleRoutes");
        _btnToggleFleets = root.Q<Button>("BtnToggleFleets");

        // --- Bind Events ---
        if (_btnPause != null) _btnPause.clicked += () => SetMultiplier(0f);
        if (_btn1x != null) _btn1x.clicked += () => SetMultiplier(1f);
        if (_btn3x != null) _btn3x.clicked += () => SetMultiplier(3f);
        if (_btn10x != null) _btn10x.clicked += () => SetMultiplier(10f);

        if (_btnInspection != null) _btnInspection.clicked += ToggleInspectionMenu;
        if (_btnToggleRoutes != null) _btnToggleRoutes.clicked += ToggleRoutes;
        if (_btnToggleFleets != null) _btnToggleFleets.clicked += ToggleFleets;
    }

    private void Update()
    {
        if (SimulationTimeManager.Instance != null && _dateLabel != null)
        {
            _dateLabel.text = $"Day {SimulationTimeManager.Instance.CurrentDay}";
            _timeLabel.text = SimulationTimeManager.Instance.GetTimeString(SimulationTimeManager.Instance.VisualTime);
            UpdateSpeedButtons(SimulationTimeManager.Instance.TimeMultiplier);
        }

        // If the menu is open, we force its position every frame (or you can do it only on toggle)
        // Doing it every frame ensures it follows the button if the layout resizes dynamically.
        if (_isInspectionOpen)
        {
            PositionDropdown();
        }
    }

    // --- Inspection Logic ---

    private void ToggleInspectionMenu()
    {
        _isInspectionOpen = !_isInspectionOpen;
        
        if (_dropdownPanel != null)
        {
            _dropdownPanel.style.display = _isInspectionOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (_isInspectionOpen) PositionDropdown();
        }

        // Toggle visual state of the main button
        if (_isInspectionOpen) _btnInspection.AddToClassList(ActiveClassName);
        else _btnInspection.RemoveFromClassList(ActiveClassName);
    }

    private void PositionDropdown()
    {
        if (_btnInspection == null || _dropdownPanel == null) return;

        // Get the button's position in screen space
        Rect btnRect = _btnInspection.worldBound;
        float screenWidth = _doc.rootVisualElement.layout.width;
        float screenHeight = _doc.rootVisualElement.layout.height;
        float panelWidth = _dropdownPanel.layout.width;
        float panelHeight = _dropdownPanel.layout.height;

        // Default: Top-Left of panel starts at Bottom-Left of button
        float finalLeft = btnRect.x;
        float finalTop = btnRect.y + btnRect.height + 5f; // 5px gap

        // Smart Check: Right Edge
        // If panel goes off screen right, align its right edge with button's right edge
        if (finalLeft + panelWidth > screenWidth)
        {
            finalLeft = btnRect.x + btnRect.width - panelWidth;
        }

        // Smart Check: Bottom Edge
        // If panel goes off screen bottom, put it ABOVE the button
        if (finalTop + panelHeight > screenHeight)
        {
            finalTop = btnRect.y - panelHeight - 5f;
        }

        // Apply
        _dropdownPanel.style.left = finalLeft;
        _dropdownPanel.style.top = finalTop;
    }

    private void ToggleRoutes()
    {
        _isRoutesVisible = !_isRoutesVisible;

        // 1. Visual Toggle (Green highlight)
        if (_isRoutesVisible) 
            _btnToggleRoutes.AddToClassList(ActiveClassName);
        else 
            _btnToggleRoutes.RemoveFromClassList(ActiveClassName);

        // 2. Logic Hook -> Call RouteVisualizer
        if (RouteVisualizer.Instance != null)
        {
            if (_isRoutesVisible)
            {
                RouteVisualizer.Instance.ShowAll();
            }
            else
            {
                RouteVisualizer.Instance.HideAll();
            }
        }
    }

    private void ToggleFleets()
    {
        _isFleetsVisible = !_isFleetsVisible;

        if (_isFleetsVisible) 
            _btnToggleFleets.AddToClassList("active"); // Turns Green
        else 
            _btnToggleFleets.RemoveFromClassList("active"); // Turns Transparent

        Debug.Log($"Fleets Toggled: {_isFleetsVisible}");
    }

    // --- Timer Logic (Existing) ---
    private void SetMultiplier(float mult) => SimulationTimeManager.Instance?.RequestTimeMultiplierRpc(mult);

    private void UpdateSpeedButtons(float currentMultiplier)
    {
        if (_btnPause == null) return;
        _btnPause.RemoveFromClassList(SelectedClassName);
        _btn1x.RemoveFromClassList(SelectedClassName);
        _btn3x.RemoveFromClassList(SelectedClassName);
        _btn10x.RemoveFromClassList(SelectedClassName);

        if (Mathf.Approximately(currentMultiplier, 0f)) _btnPause.AddToClassList(SelectedClassName);
        else if (Mathf.Approximately(currentMultiplier, 1f)) _btn1x.AddToClassList(SelectedClassName);
        else if (Mathf.Approximately(currentMultiplier, 3f)) _btn3x.AddToClassList(SelectedClassName);
        else if (Mathf.Approximately(currentMultiplier, 10f)) _btn10x.AddToClassList(SelectedClassName);
    }
}