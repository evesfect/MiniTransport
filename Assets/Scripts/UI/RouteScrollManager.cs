using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the route list panel. Populates route cards, handles selection
/// and highlighting via RouteVisualizer, and opens the edit panel.
/// </summary>
public class RouteScrollManager : MonoBehaviour
{
    [Header("References")]
    public Transform contentContainer;
    public GameObject routeCardPrefab;
    public RouteEditPanel editPanel;

    private readonly List<GameObject> _pool = new List<GameObject>();
    private string _selectedRouteID;

    private void OnEnable()
    {
        if (TransportManager.Instance != null)
            TransportManager.Instance.OnRoutesChanged += Refresh;

        // Show all routes when panel opens (if no specific selection)
        if (RouteVisualizer.Instance != null)
        {
            RouteVisualizer.Instance.ShowAll();
            if (!string.IsNullOrEmpty(_selectedRouteID))
                RouteVisualizer.Instance.HighlightRoute(_selectedRouteID);
        }

        Refresh();
    }

    private void OnDisable()
    {
        if (TransportManager.Instance != null)
            TransportManager.Instance.OnRoutesChanged -= Refresh;
    }

    public void Refresh()
    {
        var routes = TransportManager.Instance != null
            ? TransportManager.Instance.ActiveRoutes
            : new List<Route>();

        // Grow pool
        while (_pool.Count < routes.Count)
        {
            var go = Instantiate(routeCardPrefab, contentContainer);
            go.SetActive(false);
            _pool.Add(go);
        }

        for (int i = 0; i < routes.Count; i++)
        {
            _pool[i].SetActive(true);
            _pool[i].transform.SetSiblingIndex(i);
            var card = _pool[i].GetComponent<RouteCardDisplay>();
            if (card != null)
                card.Setup(routes[i], OnCardClicked, OnEditClicked);
        }

        for (int i = routes.Count; i < _pool.Count; i++)
            _pool[i].SetActive(false);

        // Reapply selection visuals
        UpdateSelectionVisuals();
    }

    private void OnCardClicked(Route route)
    {
        if (_selectedRouteID == route.RouteID)
        {
            // Deselect
            _selectedRouteID = null;
            if (RouteVisualizer.Instance != null)
                RouteVisualizer.Instance.HighlightRoute(null);
        }
        else
        {
            _selectedRouteID = route.RouteID;
            if (RouteVisualizer.Instance != null)
                RouteVisualizer.Instance.HighlightRoute(route.RouteID);
        }

        UpdateSelectionVisuals();
    }

    private void OnEditClicked(Route route)
    {
        _selectedRouteID = route.RouteID;
        UpdateSelectionVisuals();

        // Show only this route in the visualizer
        if (RouteVisualizer.Instance != null)
            RouteVisualizer.Instance.ShowOnlyRoute(route.RouteID);

        if (editPanel != null)
            editPanel.Open(route, this);
    }

    public void OnEditPanelClosed()
    {
        // Restore all routes
        if (RouteVisualizer.Instance != null)
        {
            RouteVisualizer.Instance.ShowAll();
            if (!string.IsNullOrEmpty(_selectedRouteID))
                RouteVisualizer.Instance.HighlightRoute(_selectedRouteID);
        }
        Refresh();
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var go in _pool)
        {
            if (!go.activeSelf) continue;
            var card = go.GetComponent<RouteCardDisplay>();
            if (card != null && card.CurrentRoute != null)
                card.SetSelected(card.CurrentRoute.RouteID == _selectedRouteID);
        }
    }
}
