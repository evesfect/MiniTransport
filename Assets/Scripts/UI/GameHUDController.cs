using UnityEngine;
using UnityEngine.UIElements;

public class GameHUDController : MonoBehaviour
{
    private UIDocument _doc;
    
    // Timer Elements
    private Label _dateLabel;
    private Label _timeLabel;
    private Button _btnPause, _btn1x, _btn3x, _btn10x;

    // Inspection Elements (Existing)
    private Button _btnInspection;
    private VisualElement _inspectionDropdown; // Renamed from _dropdownPanel for clarity
    private Button _btnToggleRoutes;
    private Button _btnToggleFleets;

    // Management Elements (New)
    private Button _btnManagement;
    private VisualElement _managementDropdown;
    // Add references to new buttons inside the management dropdown here
    // private Button _btnInventory; 

    private const string SelectedClassName = "selected";
    private const string ActiveClassName = "active"; 

    private bool _isInspectionOpen = false;
    private bool _isManagementOpen = false; // New state
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
        _inspectionDropdown = root.Q<VisualElement>("InspectionDropdown");
        _btnToggleRoutes = root.Q<Button>("BtnToggleRoutes");
        _btnToggleFleets = root.Q<Button>("BtnToggleFleets");

        // --- Query Management Elements (New) ---
        _btnManagement = root.Q<Button>("BtnManagement");
        _managementDropdown = root.Q<VisualElement>("ManagementDropdown");

        // --- Bind Events ---
        if (_btnPause != null) _btnPause.clicked += () => SetMultiplier(0f);
        if (_btn1x != null) _btn1x.clicked += () => SetMultiplier(1f);
        if (_btn3x != null) _btn3x.clicked += () => SetMultiplier(3f);
        if (_btn10x != null) _btn10x.clicked += () => SetMultiplier(10f);

        if (_btnInspection != null) _btnInspection.clicked += ToggleInspectionMenu;
        if (_btnToggleRoutes != null) _btnToggleRoutes.clicked += ToggleRoutes;
        if (_btnToggleFleets != null) _btnToggleFleets.clicked += ToggleFleets;

        // Bind New Management Button
        if (_btnManagement != null) _btnManagement.clicked += ToggleManagementMenu;
    }

    private void Update()
    {
        if (SimulationTimeManager.Instance != null && _dateLabel != null)
        {
            _dateLabel.text = $"Day {SimulationTimeManager.Instance.CurrentDay}";
            _timeLabel.text = SimulationTimeManager.Instance.GetTimeString(SimulationTimeManager.Instance.VisualTime);
            UpdateSpeedButtons(SimulationTimeManager.Instance.TimeMultiplier);
        }

        // Position active dropdowns every frame
        if (_isInspectionOpen) PositionDropdown(_btnInspection, _inspectionDropdown);
        if (_isManagementOpen) PositionDropdown(_btnManagement, _managementDropdown);
    }

    // --- Menu Logic ---

    private void ToggleInspectionMenu()
    {
        _isInspectionOpen = !_isInspectionOpen;
        
        // Close other menus if open
        if (_isInspectionOpen) CloseManagementMenu();

        UpdateMenuState(_btnInspection, _inspectionDropdown, _isInspectionOpen);
    }

    private void ToggleManagementMenu()
    {
        _isManagementOpen = !_isManagementOpen;

        // Close other menus if open
        if (_isManagementOpen) CloseInspectionMenu();

        UpdateMenuState(_btnManagement, _managementDropdown, _isManagementOpen);
    }

    private void CloseInspectionMenu()
    {
        _isInspectionOpen = false;
        UpdateMenuState(_btnInspection, _inspectionDropdown, false);
    }

    private void CloseManagementMenu()
    {
        _isManagementOpen = false;
        UpdateMenuState(_btnManagement, _managementDropdown, false);
    }

    private void UpdateMenuState(Button btn, VisualElement panel, bool isOpen)
    {
        if (panel != null)
        {
            panel.style.display = isOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (btn != null)
        {
            if (isOpen) btn.AddToClassList(ActiveClassName);
            else btn.RemoveFromClassList(ActiveClassName);
        }
    }

    // Refactored to be generic
    private void PositionDropdown(Button targetBtn, VisualElement targetPanel)
    {
        if (targetBtn == null || targetPanel == null) return;

        Rect btnRect = targetBtn.worldBound;
        float screenWidth = _doc.rootVisualElement.layout.width;
        float screenHeight = _doc.rootVisualElement.layout.height;
        float panelWidth = targetPanel.layout.width;
        float panelHeight = targetPanel.layout.height;

        float finalLeft = btnRect.x;
        float finalTop = btnRect.y + btnRect.height + 5f; 

        // Smart Check: Right Edge
        if (finalLeft + panelWidth > screenWidth)
        {
            finalLeft = btnRect.x + btnRect.width - panelWidth;
        }

        // Smart Check: Bottom Edge
        if (finalTop + panelHeight > screenHeight)
        {
            finalTop = btnRect.y - panelHeight - 5f;
        }

        targetPanel.style.left = finalLeft;
        targetPanel.style.top = finalTop;
    }

    // --- Inspection Actions ---

    private void ToggleRoutes()
    {
        _isRoutesVisible = !_isRoutesVisible;
        if (_isRoutesVisible) _btnToggleRoutes.AddToClassList(ActiveClassName);
        else _btnToggleRoutes.RemoveFromClassList(ActiveClassName);

        if (RouteVisualizer.Instance != null)
        {
            if (_isRoutesVisible) RouteVisualizer.Instance.ShowAll();
            else RouteVisualizer.Instance.HideAll();
        }
    }

    private void ToggleFleets()
    {
        _isFleetsVisible = !_isFleetsVisible;
        if (_isFleetsVisible) _btnToggleFleets.AddToClassList(ActiveClassName);
        else _btnToggleFleets.RemoveFromClassList(ActiveClassName);
        
        Debug.Log($"Fleets Toggled: {_isFleetsVisible}");
    }

    // --- Timer Logic ---
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