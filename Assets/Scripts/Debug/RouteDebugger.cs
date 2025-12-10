using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(LineRenderer))]
public class RouteDebugger : MonoBehaviour
{
    [Header("Route Settings")]
    public string routeName = "Test Route";
    public Color routeColor = Color.cyan;

    public float lineRendererHeight = 5f;
    public float lineRendererWidth = 1f;
    
    [Header("Editor Data")]
    [Tooltip("Drag stops here to define the route path.")]
    public List<BusStop> inputStops = new List<BusStop>();

    [Header("Runtime State (Read Only)")]
    [SerializeField] private Route _activeRouteInstance; // The route object inside TransportManager

    private LineRenderer lr;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = lineRendererWidth;
        lr.material = new Material(Shader.Find("Sprites/Default"));
    }

    [ContextMenu("1. Create or Update Route")]
    public void CreateOrUpdateRoute()
    {
        if (TransportManager.Instance == null)
        {
            Debug.LogError("TransportManager missing in scene!");
            return;
        }

        List<string> ids = new List<string>();
        foreach (var stop in inputStops)
        {
            if (stop != null) ids.Add(stop.stopID);
        }

        if (ids.Count < 2)
        {
            Debug.LogWarning("Need at least 2 stops to make a route.");
            return;
        }

        if (_activeRouteInstance != null && TransportManager.Instance.ActiveRoutes.Contains(_activeRouteInstance))
        {
            // EDIT EXISTING (Simulate In-Game Edit)
            _activeRouteInstance.StopIDs = new List<string>(ids);
            _activeRouteInstance.RouteName = routeName;
            _activeRouteInstance.RouteColor = routeColor;
            Debug.Log($"Updated existing route: {_activeRouteInstance.RouteID}");
        }
        else
        {
            _activeRouteInstance = TransportManager.Instance.CreateRoute(routeName, ids, routeColor);
            Debug.Log($"Created new route: {_activeRouteInstance.RouteID}");
        }
        RefreshVisuals();
    }

    [ContextMenu("3. Save to Disk")]
    public void TestSave()
    {
        TransportManager.Instance.SaveRoutes();
    }

    [ContextMenu("4. Load from Disk")]
    public void TestLoad()
    {
        _activeRouteInstance = null;
        inputStops.Clear();
        lr.positionCount = 0;

        TransportManager.Instance.LoadRoutes();

        if (TransportManager.Instance.ActiveRoutes.Count > 0)
        {
            // Pick the first one for visualization
            _activeRouteInstance = TransportManager.Instance.ActiveRoutes[0];
            
            // Sync Inspector List back from loaded data
            inputStops.Clear();
            foreach(string id in _activeRouteInstance.StopIDs)
            {
                BusStop stop = TransportManager.Instance.GetStop(id);
                if(stop != null) inputStops.Add(stop);
            }

            Debug.Log($"Loaded {_activeRouteInstance.RouteName} with {_activeRouteInstance.StopIDs.Count} stops.");
            RefreshVisuals();
        }
        else
        {
            Debug.LogWarning("Loaded file, but no routes found.");
        }
    }

    // helper
    private void RefreshVisuals()
    {
        if (_activeRouteInstance == null) return;

        lr.startColor = _activeRouteInstance.RouteColor;
        lr.endColor = _activeRouteInstance.RouteColor;

        List<Vector3> pathPoints = new List<Vector3>();

        for (int i = 0; i < _activeRouteInstance.StopIDs.Count - 1; i++)
        {
            BusStop start = TransportManager.Instance.GetStop(_activeRouteInstance.StopIDs[i]);
            BusStop end = TransportManager.Instance.GetStop(_activeRouteInstance.StopIDs[i+1]);

            if (start == null || end == null) continue;

            pathPoints.Add(start.transform.position + Vector3.up * lineRendererHeight);

            // Pathfinding Logic
            if (start.parentSegment != null && end.parentSegment != null)
            {
                RoadNode pathStart = start.parentSegment.NodeB; 
                RoadNode pathEnd = end.parentSegment.NodeA;

                if (pathStart != null && pathEnd != null)
                {
                    var nodePath = RoadPathfinder.FindPath(pathStart, pathEnd);
                    if (nodePath != null)
                    {
                        foreach (var node in nodePath)
                            pathPoints.Add(node.transform.position + Vector3.up * lineRendererHeight);
                    }
                }
            }

            pathPoints.Add(end.transform.position + Vector3.up * lineRendererHeight);
        }

        lr.positionCount = pathPoints.Count;
        lr.SetPositions(pathPoints.ToArray());
    }
}