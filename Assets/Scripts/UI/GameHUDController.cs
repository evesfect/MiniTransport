using System.Linq;
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
    private VisualElement _inspectionDropdown;
    private Button _btnToggleRoutes;
    private Button _btnToggleFleets;

    // Management Elements
    private Button _btnManagement;
    private VisualElement _managementDropdown;
    private Button _btnBusStops;

    // BusStopPanel
    private VisualElement _busStopsPanel;
    private Button _btnSortWaiting;
    private Button _btnSortBoarded;

    private ScrollView _scrollWaiting;
    private ScrollView _scrollBoarded;

    private bool _sortWaitingAsc = false;
    private bool _sortBoardedAsc = false;

    private const string SelectedClassName = "selected";
    private const string ActiveClassName = "active"; 

    private bool _isInspectionOpen = false;
    private bool _isManagementOpen = false;
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

        // --- Query Management Elements ---
        _btnManagement = root.Q<Button>("BtnManagement");
        _managementDropdown = root.Q<VisualElement>("ManagementDropdown");
        
        // Query Bus Stops Button
        _btnBusStops = root.Q<Button>("BtnOpenBusStops");

        // Query Bus Stops Panel + Elements
        _busStopsPanel = root.Q<VisualElement>("BusStopsPanel");
        _scrollWaiting = root.Q<ScrollView>("ScrollWaiting");
        _scrollBoarded = root.Q<ScrollView>("ScrollBoarded");
        _btnSortWaiting = root.Q<Button>("BtnSortWaiting");
        _btnSortBoarded = root.Q<Button>("BtnSortBoarded");
        SetupPanel(_busStopsPanel);

        // --- Bind Events ---
        if (_btnPause != null) _btnPause.clicked += () => SetMultiplier(0f);
        if (_btn1x != null) _btn1x.clicked += () => SetMultiplier(1f);
        if (_btn3x != null) _btn3x.clicked += () => SetMultiplier(3f);
        if (_btn10x != null) _btn10x.clicked += () => SetMultiplier(10f);

        if (_btnInspection != null) _btnInspection.clicked += ToggleInspectionMenu;
        if (_btnToggleRoutes != null) _btnToggleRoutes.clicked += ToggleRoutes;
        if (_btnToggleFleets != null) _btnToggleFleets.clicked += ToggleFleets;

        if (_btnManagement != null) _btnManagement.clicked += ToggleManagementMenu;
        if (_btnBusStops != null) _btnBusStops.clicked += () => OpenPanel(_busStopsPanel);
        if (_btnSortWaiting != null)
            _btnSortWaiting.clicked += () => ToggleSort(ref _sortWaitingAsc, _btnSortWaiting, _scrollWaiting);
        
        if (_btnSortBoarded != null) 
            _btnSortBoarded.clicked += () => ToggleSort(ref _sortBoardedAsc, _btnSortBoarded, _scrollBoarded);
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
        if (_isInspectionOpen) CloseManagementMenu();
        UpdateMenuState(_btnInspection, _inspectionDropdown, _isInspectionOpen);
    }

    private void ToggleManagementMenu()
    {
        _isManagementOpen = !_isManagementOpen;
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
        if (panel != null) panel.style.display = isOpen ? DisplayStyle.Flex : DisplayStyle.None;
        if (btn != null)
        {
            if (isOpen) btn.AddToClassList(ActiveClassName);
            else btn.RemoveFromClassList(ActiveClassName);
        }
    }

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

        if (finalLeft + panelWidth > screenWidth) finalLeft = btnRect.x + btnRect.width - panelWidth;
        if (finalTop + panelHeight > screenHeight) finalTop = btnRect.y - panelHeight - 5f;

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

    // --- Panel Logic ---

    private void SetupPanel(VisualElement panel)
    {
        if (panel == null) return;

        // Setup Close Button
        var btnClose = panel.Q<Button>(className: "close-btn");
        if (btnClose != null)
        {
            btnClose.clicked += () => panel.style.display = DisplayStyle.None;
        }

        // Setup Dragging
        var header = panel.Q<VisualElement>(className: "panel-header");
        if (header != null)
        {
            new PanelDragger(panel, header);
        }
    }

    private void OpenPanel(VisualElement panel)
    {
        if (panel == null) return;
        panel.style.display = DisplayStyle.Flex;
        panel.BringToFront();
        CloseManagementMenu();
    }

    private void ToggleSort(ref bool isAscending, Button btn, ScrollView scroll)
    {
        isAscending = !isAscending;

        // 1. Visual Update
        if (isAscending)
        {
            btn.RemoveFromClassList("icon-sort-desc");
            btn.AddToClassList("icon-sort-asc");
        }
        else
        {
            btn.RemoveFromClassList("icon-sort-asc");
            btn.AddToClassList("icon-sort-desc");
        }

        // 2. Sorting Logic
        if (scroll != null)
        {
            SortList(scroll, isAscending);
        }
    }
    private void SortList(ScrollView scroll, bool ascending)
    {
        // Get all "list-item" children
        // contentContainer is the internal element of ScrollView that holds the items
        var items = scroll.contentContainer.Children().ToList();

        items.Sort((a, b) => 
        {
            // Find the count labels inside the items
            var labelA = a.Q<Label>(className: "item-count");
            var labelB = b.Q<Label>(className: "item-count");

            int valA = ParseCount(labelA?.text);
            int valB = ParseCount(labelB?.text);

            return ascending ? valA.CompareTo(valB) : valB.CompareTo(valA);
        });

        // Clear and Re-add in new order
        scroll.contentContainer.Clear();
        foreach (var item in items)
        {
            scroll.contentContainer.Add(item);
        }
    }

    private int ParseCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        // Remove commas (e.g. "1,200" -> "1200") to parse correctly
        string cleanText = text.Replace(",", "").Trim();
        
        if (int.TryParse(cleanText, out int result))
        {
            return result;
        }
        return 0;
    }
}

// --- Panel Dragger Class ---
public class PanelDragger
{
    private readonly VisualElement _target;
    private readonly VisualElement _handle;
    private bool _isDragging;
    private Vector2 _startMousePosition;
    private Vector2 _startElementPosition;

    public PanelDragger(VisualElement target, VisualElement handle)
    {
        _target = target;
        _handle = handle;

        _handle.RegisterCallback<PointerDownEvent>(evt => {
            if (evt.button != 0) return;
            _isDragging = true;
            _handle.CapturePointer(evt.pointerId);
            _startMousePosition = evt.position;
            _startElementPosition = new Vector2(_target.resolvedStyle.left, _target.resolvedStyle.top);
            evt.StopPropagation();
        });

        _handle.RegisterCallback<PointerMoveEvent>(evt => {
            if (!_isDragging || !_handle.HasPointerCapture(evt.pointerId)) return;
            Vector2 diff = (Vector2)evt.position - _startMousePosition;
            _target.style.left = _startElementPosition.x + diff.x;
            _target.style.top = _startElementPosition.y + diff.y;
        });

        _handle.RegisterCallback<PointerUpEvent>(evt => {
            if (!_isDragging) return;
            _isDragging = false;
            _handle.ReleasePointer(evt.pointerId);
        });
    }
}