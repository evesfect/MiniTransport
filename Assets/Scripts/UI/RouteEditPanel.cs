using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Edit panel for a single route. Shows bus stop list (reorderable via drag),
/// assigned buses list, delete stop buttons, and an add-stop tool toggle.
/// </summary>
public class RouteEditPanel : MonoBehaviour
{
    [Header("Panel")]
    public GameObject panelRoot;
    public TMP_Text routeNameTitle;
    public Button closeButton;

    [Header("Bus Stop List")]
    public Transform stopListContainer;
    public GameObject stopCardPrefab;

    [Header("Assigned Buses List")]
    public Transform busListContainer;
    public GameObject busAssignedCardPrefab;

    [Header("Add Stop Tool")]
    public Toggle addStopToggle;
    public BusStopAddTool busStopAddTool;

    private Route _currentRoute;
    public Route CurrentRoute => _currentRoute;
    private RouteScrollManager _parentManager;
    private readonly List<GameObject> _stopPool = new List<GameObject>();
    private readonly List<GameObject> _busPool = new List<GameObject>();
    private CanvasGroup _canvasGroup;

    private void Awake()
    {
        if (panelRoot == null)
        {
            Debug.LogWarning($"[RouteEditPanel] panelRoot is not assigned on {gameObject.name}. " +
                "Assign a dedicated child GameObject as panelRoot so it doesn't conflict with BasePanel's CanvasGroup.");
        }

        _canvasGroup = PanelObject.GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = PanelObject.AddComponent<CanvasGroup>();

        HidePanel();

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (addStopToggle != null)
        {
            addStopToggle.onValueChanged.AddListener(OnAddStopToggled);
            addStopToggle.isOn = false;
        }
    }

    private void ShowPanel()
    {
        _canvasGroup.alpha = 1f;
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;
    }

    private void HidePanel()
    {
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;
    }

    private GameObject PanelObject => panelRoot != null ? panelRoot : gameObject;

    public void Open(Route route, RouteScrollManager parent)
    {
        _currentRoute = route;
        _parentManager = parent;

        ShowPanel();

        if (routeNameTitle != null)
            routeNameTitle.text = route.RouteName;

        if (addStopToggle != null)
            addStopToggle.isOn = false;

        RefreshStopList();
        RefreshBusList();
    }

    public void Close()
    {
        if (addStopToggle != null)
            addStopToggle.isOn = false;

        HidePanel();

        _parentManager?.OnEditPanelClosed();
    }

    // ────── Bus Stop List ──────

    public void RefreshStopList()
    {
        if (_currentRoute == null) return;

        while (_stopPool.Count < _currentRoute.StopIDs.Count)
        {
            var go = Instantiate(stopCardPrefab, stopListContainer);
            go.SetActive(false);

            var drag = go.GetComponent<BusStopDragHandler>();
            if (drag != null)
                drag.OnOrderChanged += CommitStopOrder;

            _stopPool.Add(go);
        }

        for (int i = 0; i < _currentRoute.StopIDs.Count; i++)
        {
            _stopPool[i].SetActive(true);
            _stopPool[i].transform.SetSiblingIndex(i);

            var card = _stopPool[i].GetComponent<BusStopCardDisplay>();
            if (card != null)
            {
                string stopID = _currentRoute.StopIDs[i];
                BusStop stop = TransportManager.Instance?.GetStop(stopID);
                string displayName = stop != null ? stop.gameObject.name : stopID;
                card.Setup(stopID, displayName, i + 1, OnDeleteStop);
            }
        }

        for (int i = _currentRoute.StopIDs.Count; i < _stopPool.Count; i++)
            _stopPool[i].SetActive(false);
    }

    private void CommitStopOrder()
    {
        if (_currentRoute == null) return;

        var reorderedIDs = new List<string>();
        for (int i = 0; i < stopListContainer.childCount; i++)
        {
            var child = stopListContainer.GetChild(i);
            if (!child.gameObject.activeSelf) continue;
            var card = child.GetComponent<BusStopCardDisplay>();
            if (card != null)
                reorderedIDs.Add(card.StopID);
        }

        _currentRoute.StopIDs = reorderedIDs;
        TransportManager.Instance?.UpdateRouteClient(_currentRoute);

        RefreshStopList();
        RefreshRouteVisualization();
    }

    private void OnDeleteStop(string stopID)
    {
        if (_currentRoute == null) return;

        _currentRoute.StopIDs.Remove(stopID);
        TransportManager.Instance?.UpdateRouteClient(_currentRoute);

        RefreshStopList();
        RefreshRouteVisualization();
    }

    /// <summary>
    /// Called by BusStopAddTool when a new stop is selected via left click.
    /// </summary>
    public void AddStop(string stopID)
    {
        if (_currentRoute == null) return;
        if (_currentRoute.StopIDs.Contains(stopID)) return;

        _currentRoute.StopIDs.Add(stopID);
        TransportManager.Instance?.UpdateRouteClient(_currentRoute);

        RefreshStopList();
        RefreshRouteVisualization();
    }

    /// <summary>
    /// Called by BusStopAddTool when a stop is right-clicked to remove.
    /// </summary>
    public void RemoveStop(string stopID)
    {
        if (_currentRoute == null) return;
        if (!_currentRoute.StopIDs.Contains(stopID)) return;

        _currentRoute.StopIDs.Remove(stopID);
        TransportManager.Instance?.UpdateRouteClient(_currentRoute);

        RefreshStopList();
        RefreshRouteVisualization();
    }

    private void RefreshRouteVisualization()
    {
        if (_currentRoute == null || RouteVisualizer.Instance == null) return;
        if (_currentRoute.StopIDs.Count < 2)
        {
            RouteVisualizer.Instance.HideRoute(_currentRoute.RouteID);
            return;
        }
        RouteVisualizer.Instance.ShowOnlyRoute(_currentRoute.RouteID);
    }

    // ────── Assigned Buses List ──────

    private void RefreshBusList()
    {
        if (_currentRoute == null || FleetManager.Instance == null) return;

        var assignedBuses = new List<BusData>();
        foreach (var bus in FleetManager.Instance.allBuses)
        {
            if (bus.Schedule != null && bus.Schedule.RouteID == _currentRoute.RouteID)
                assignedBuses.Add(bus);
        }

        while (_busPool.Count < assignedBuses.Count)
        {
            var go = Instantiate(busAssignedCardPrefab, busListContainer);
            go.SetActive(false);
            _busPool.Add(go);
        }

        for (int i = 0; i < assignedBuses.Count; i++)
        {
            _busPool[i].SetActive(true);
            _busPool[i].transform.SetSiblingIndex(i);

            var card = _busPool[i].GetComponent<AssignedBusCardDisplay>();
            if (card != null)
                card.Setup(assignedBuses[i]);
        }

        for (int i = assignedBuses.Count; i < _busPool.Count; i++)
            _busPool[i].SetActive(false);
    }

    // ────── Add Stop Toggle ──────

    private void OnAddStopToggled(bool isOn)
    {
        if (busStopAddTool == null) return;

        if (isOn)
            busStopAddTool.Activate(this);
        else
            busStopAddTool.Deactivate();
    }

    private void OnDisable()
    {
        if (addStopToggle != null)
            addStopToggle.isOn = false;
    }
}
