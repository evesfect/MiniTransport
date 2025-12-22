using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;

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

    private VisualElement _fleetPanel;
    private ScrollView _fleetListContainer;
    private Coroutine _refreshCoroutine;

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

        // --- Query Fleet Panel ---
        _fleetPanel = root.Q<VisualElement>("FleetListPanel");
        _fleetListContainer = root.Q<ScrollView>("FleetListContainer");

        // --- Bind Events ---
        if (_btnPause != null) _btnPause.clicked += () => SetMultiplier(0f);
        if (_btn1x != null) _btn1x.clicked += () => SetMultiplier(1f);
        if (_btn3x != null) _btn3x.clicked += () => SetMultiplier(3f);
        if (_btn10x != null) _btn10x.clicked += () => SetMultiplier(10f);

        if (_btnInspection != null) _btnInspection.clicked += ToggleInspectionMenu;
        if (_btnToggleRoutes != null) _btnToggleRoutes.clicked += ToggleRoutes;
        if (_btnToggleFleets != null) _btnToggleFleets.clicked += ToggleFleets;
    }

    private void OnDisable()
    {
        if (_refreshCoroutine != null) StopCoroutine(_refreshCoroutine);
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

        // Visual Toggle on Button
        if (_isFleetsVisible)
            _btnToggleFleets.AddToClassList(ActiveClassName);
        else
            _btnToggleFleets.RemoveFromClassList(ActiveClassName);

        // Show/Hide Panel
        if (_fleetPanel != null)
        {
            _fleetPanel.style.display = _isFleetsVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Start/Stop Auto-Refresh
        if (_isFleetsVisible)
        {
            RefreshFleetList(); // Initial Update
            if (_refreshCoroutine == null) _refreshCoroutine = StartCoroutine(AutoRefreshFleetUI());
        }
        else
        {
            if (_refreshCoroutine != null)
            {
                StopCoroutine(_refreshCoroutine);
                _refreshCoroutine = null;
            }
        }
    }

    private IEnumerator AutoRefreshFleetUI()
    {
        while (_isFleetsVisible)
        {
            yield return new WaitForSeconds(1.0f); // Refresh every second
            RefreshFleetList();
        }
    }

    private void RefreshFleetList()
    {
        if (_fleetListContainer == null || FleetManager.Instance == null) return;

        _fleetListContainer.Clear();

        foreach (var busData in FleetManager.Instance.allBuses)
        {
            // Create Row
            VisualElement row = new VisualElement();
            row.AddToClassList("fleet-row");

            // Columns
            Label lblId = new Label(busData.BusID);
            lblId.AddToClassList("fleet-col-id");

            Label lblStatus = new Label();
            lblStatus.AddToClassList("fleet-col-status");

            Label lblHealth = new Label($"{busData.Durability:F0}%");
            lblHealth.AddToClassList("fleet-col-health");

            // Determine Runtime State
            string statusText = "Depot";
            string statusClass = "status-idle";

            // Check if bus is spawned
            GameObject activeBus = FleetManager.Instance.GetActiveBus(busData.BusID);
            if (activeBus != null)
            {
                var driver = activeBus.GetComponent<BusDriver>();
                if (driver != null)
                {
                    if (driver.IsBroken)
                    {
                        statusText = "BROKEN";
                        statusClass = "status-broken";
                    }
                    else
                    {
                        statusText = "In Service";
                        statusClass = "status-ok";
                    }
                }
            }

            lblStatus.text = statusText;
            lblStatus.AddToClassList(statusClass);

            // Assemble
            row.Add(lblId);
            row.Add(lblStatus);
            row.Add(lblHealth);

            _fleetListContainer.Add(row);
        }
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