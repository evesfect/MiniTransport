using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class RouteDebugger : MonoBehaviour
{
    [Header("Selection")]
    public SelectionBoxController selectionController;
    // --- UI State ---
    private Rect _windowRect = new Rect(20, 20, 320, 850);

    private const float COLLAPSED_HEIGHT = 60f;
    private const float EXPANDED_HEIGHT = 850f;
    private bool _isCollapsed = true;
    private string _targetRouteName = "Route 1";
    
    // Simple color picker state (RGBA 0-1)
    private float _r = 0, _g = 1, _b = 1; 

    // Stop editing state
    private List<string> _currentStopIDs = new List<string>();
    private string _inputStopID = "";

    // Visualization
    public float baseHeight = 5f;
    public float heightStep = 2f; 
    public float lineWidth = 1f;
    private List<GameObject> _spawnedVisuals = new List<GameObject>();
    private Vector2 _scrollPos; // For the route list

    private void OnGUI()
    {
        _windowRect.height = _isCollapsed ? COLLAPSED_HEIGHT : EXPANDED_HEIGHT;
        _windowRect = GUI.Window(0, _windowRect, DrawWindow, "");
    }

    private void DrawWindow(int windowID)
    {
        if (TransportManager.Instance == null)
        {
            GUILayout.Label("Waiting for TransportManager...");
            GUI.DragWindow();
            return;
        }

        GUILayout.BeginVertical();

        // --- SECTION: COLLAPSE TOGGLE ---

        GUILayout.BeginHorizontal(GUI.skin.box);

        if (GUILayout.Button(_isCollapsed ? "▶" : "▼", GUILayout.Width(25)))
        {
            _isCollapsed = !_isCollapsed;
        }

        GUILayout.Label("Route Debugger (Runtime)", GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();

        // COLLAPSED: close layout properly before returning
        if (_isCollapsed)
        {
            GUILayout.EndVertical();
            GUI.DragWindow();
            return;
        }

        // --- SECTION: AVAILABLE ROUTES LIST ---
        GUILayout.Label("Available Routes:", GUI.skin.box);
        
        _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(100));
        if (TransportManager.Instance.ActiveRoutes.Count == 0)
        {
            GUILayout.Label("(No Routes Found)");
        }
        else
        {
            foreach (var r in TransportManager.Instance.ActiveRoutes)
            {
                if (GUILayout.Button($"{r.RouteName} ({r.StopIDs.Count} stops)"))
                {
                    _targetRouteName = r.RouteName;
                    LoadRouteToGUI(); // Auto-load when clicked
                }
            }
        }
        GUILayout.EndScrollView();
        
        GUILayout.Space(10);

        // --- SECTION: ROUTE EDITOR ---
        GUILayout.Label("Route Configuration", GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Name:", GUILayout.Width(50));
        _targetRouteName = GUILayout.TextField(_targetRouteName);
        GUILayout.EndHorizontal();

        // Color Picker
        GUILayout.Label("Color (R/G/B):");
        GUILayout.BeginHorizontal();
        _r = GUILayout.HorizontalSlider(_r, 0f, 1f);
        _g = GUILayout.HorizontalSlider(_g, 0f, 1f);
        _b = GUILayout.HorizontalSlider(_b, 0f, 1f);
        GUILayout.EndHorizontal();
        Color currentColor = new Color(_r, _g, _b);
        GUI.color = currentColor;
        GUILayout.Button("Color Preview"); 
        GUI.color = Color.white;

        GUILayout.Space(10);

        // Stop List Management
        GUILayout.Label($"Stops in List: {_currentStopIDs.Count}", GUI.skin.box);
        
        // Input for Stop ID
        GUILayout.BeginHorizontal();
        _inputStopID = GUILayout.TextField(_inputStopID);
        if (GUILayout.Button("Add ID", GUILayout.Width(60)))
        {
            if (!string.IsNullOrEmpty(_inputStopID))
            {
                _currentStopIDs.Add(_inputStopID);
                _inputStopID = "";
            }
        }
        GUILayout.EndHorizontal();

        // Selected Bus Stop List
        GUILayout.Space(10);
        GUILayout.Label("Selected BusStops", GUI.skin.box);

        GUILayout.BeginVertical(GUI.skin.box);

        var selectedStops = GetSelectedBusStops();

        if (selectedStops.Count == 0)
        {
            GUILayout.Label("(No BusStops selected)");
        }
        else
        {
            foreach (var stop in selectedStops)
            {
                bool alreadyInRoute = _currentStopIDs.Contains(stop.stopID);

                GUI.color = alreadyInRoute ? Color.yellow : Color.white;
                GUILayout.Label($"• {stop.name} ({stop.stopID})");
                GUI.color = Color.white;
            }
        }

        GUILayout.EndVertical();

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Add Selected"))
        {
            AddSelectedBusStops();
        }

        if (GUILayout.Button("Remove Selected"))
        {
            RemoveSelectedBusStops();
        }

        GUILayout.EndHorizontal();



        if (GUILayout.Button("Clear Stops"))
        {
            _currentStopIDs.Clear();
        }

        // List Preview (Show last 5 to save space)
        GUILayout.BeginVertical(GUI.skin.box);
        if (_currentStopIDs.Count == 0) GUILayout.Label("(Empty)");
        for(int i=0; i<_currentStopIDs.Count; i++)
        {
            GUILayout.Label($"{i+1}. {_currentStopIDs[i]}");
        }
        GUILayout.EndVertical();

        GUILayout.Space(15);

        // CRUD Operations
        GUILayout.Label("Actions", GUI.skin.box);

        if (GUILayout.Button("Create / Update Route"))
        {
            CreateOrUpdateRoute(currentColor);
        }

        // 'Load Route' button is now redundant because of the list, but kept for manual name entry
        if (GUILayout.Button("Revert / Reload"))
        {
            LoadRouteToGUI();
        }

        if (GUILayout.Button("Delete Route"))
        {
            DeleteRoute();
        }

        GUILayout.Space(10);
        GUI.backgroundColor = Color.yellow;
        if (GUILayout.Button("Visualize All (Local)"))
        {
            VisualizeAllRoutes();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.EndVertical();
        GUI.DragWindow();
    }

    private void AddSelectedStop()
    {
        if (selectionController == null)
        {
            Debug.LogWarning("SelectionBoxController not assigned.");
            return;
        }

        int addedCount = 0;

        foreach (GameObject obj in selectionController.selectedObjects)
        {
            if (obj == null) continue;

            BusStop stop = obj.GetComponent<BusStop>();
            if (stop == null) continue;

            if (!_currentStopIDs.Contains(stop.stopID))
            {
                _currentStopIDs.Add(stop.stopID);
                addedCount++;
            }
        }

        Debug.Log($"Added {addedCount} BusStop(s) from selection.");
    }

    private void AddSelectedBusStops()
    {
        var selectedStops = GetSelectedBusStops();
        int added = 0;

        foreach (var stop in selectedStops)
        {
            if (!_currentStopIDs.Contains(stop.stopID))
            {
                _currentStopIDs.Add(stop.stopID);
                added++;
            }
        }

        Debug.Log($"Added {added} BusStop(s).");
    }
    private void RemoveSelectedBusStops()
    {
        var selectedStops = GetSelectedBusStops();
        int removed = 0;

        foreach (var stop in selectedStops)
        {
            if (_currentStopIDs.Remove(stop.stopID))
                removed++;
        }

        Debug.Log($"Removed {removed} BusStop(s).");
    }



    private List<BusStop> GetSelectedBusStops()
    {
        if (selectionController == null)
            return new List<BusStop>();

        return selectionController.selectedObjects
            .Where(o => o != null)
            .Select(o => o.GetComponent<BusStop>())
            .Where(bs => bs != null)
            .ToList();
    }

    private void CreateOrUpdateRoute(Color col)
    {
        if (_currentStopIDs.Count < 2)
        {
            Debug.LogWarning("Need at least 2 stops.");
            return;
        }

        Route existing = TransportManager.Instance.ActiveRoutes.FirstOrDefault(r => r.RouteName == _targetRouteName);

        if (existing != null)
        {
            Route updatedRoute = new Route(_targetRouteName, _currentStopIDs, col);
            updatedRoute.RouteID = existing.RouteID; 
            
            TransportManager.Instance.UpdateRouteClient(updatedRoute);
            Debug.Log($"Requesting Update for: {_targetRouteName}");
        }
        else
        {
            Route newRoute = new Route(_targetRouteName, _currentStopIDs, col);
            TransportManager.Instance.AddRouteClient(newRoute);
            Debug.Log($"Requesting Create: {_targetRouteName}");
        }
    }

    private void LoadRouteToGUI()
    {
        Route r = TransportManager.Instance.ActiveRoutes.FirstOrDefault(r => r.RouteName == _targetRouteName);
        if (r == null)
        {
            Debug.LogWarning($"Route '{_targetRouteName}' not found.");
            return;
        }

        _currentStopIDs = new List<string>(r.StopIDs);
        _r = r.RouteColor.r;
        _g = r.RouteColor.g;
        _b = r.RouteColor.b;
    }

    private void DeleteRoute()
    {
        Route r = TransportManager.Instance.ActiveRoutes.FirstOrDefault(r => r.RouteName == _targetRouteName);
        if (r != null)
        {
            TransportManager.Instance.DeleteRouteClient(r);
            Debug.Log($"Requesting Delete: '{_targetRouteName}'");
        }
    }

    private void VisualizeAllRoutes()
    {
        foreach (var obj in _spawnedVisuals)
        {
            if (obj != null) Destroy(obj);
        }
        _spawnedVisuals.Clear();

        if (TransportManager.Instance == null) return;

        TransportManager.Instance.RecalculateAllPaths();

        int index = 0;
        foreach (var route in TransportManager.Instance.ActiveRoutes)
        {
            float h = baseHeight + (index * heightStep);
            DrawSingleRoute(route, h);
            index++;
        }
    }

    private void DrawSingleRoute(Route route, float heightOffset)
    {
        GameObject lineObj = new GameObject($"Viz_{route.RouteName}");
        lineObj.transform.parent = transform;
        _spawnedVisuals.Add(lineObj);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = lineWidth;
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.renderQueue = 4000;
        lr.material = mat;
        lr.startColor = route.RouteColor;
        lr.endColor = route.RouteColor;

        List<Vector3> points = new List<Vector3>();

        for (int i = 0; i < route.StopIDs.Count - 1; i++)
        {
            BusStop start = TransportManager.Instance.GetStop(route.StopIDs[i]);
            BusStop end = TransportManager.Instance.GetStop(route.StopIDs[i + 1]);

            if (start == null || end == null) continue;

            points.Add(start.transform.position + Vector3.up * heightOffset);

            List<RoadNode> path = TransportManager.Instance.GetPath(start, end);
            if (path != null)
            {
                foreach (var node in path)
                {
                    points.Add(node.transform.position + Vector3.up * heightOffset);
                }
            }
        }

        if (route.StopIDs.Count > 0)
        {
            BusStop last = TransportManager.Instance.GetStop(route.StopIDs.Last());
            if (last != null) points.Add(last.transform.position + Vector3.up * heightOffset);
        }

        lr.positionCount = points.Count;
        lr.SetPositions(points.ToArray());
    }
}