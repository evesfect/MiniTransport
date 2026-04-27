using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Splines;

public class RouteVisualizer : MonoBehaviour
{
    public static RouteVisualizer Instance { get; private set; }

    [Header("Line Settings")]
    public float lineWidth = 0.8f;
    public float baseHeight = 2.0f; 
    public float heightStep = 1.5f;
    
    [Header("Stop Marker Settings")]
    public float markerSize = 1.5f; 
    public float innerMarkerRatio = 0.5f;

    [Header("Corner Smoothing")]
    public float cornerRadius = 2.0f; 
    public int cornerResolution = 5;

    [Header("Resources")]
    public Material routeMaterial; 
    private Material _markerMatOuter; 
    private Material _markerMatInner; 

    private Dictionary<string, LineRenderer> _activeLines = new Dictionary<string, LineRenderer>();
    private Dictionary<string, Color> _originalColors = new Dictionary<string, Color>();
    private string _highlightedRouteID;

    public static readonly Color GreyedOutColor = new Color(0.45f, 0.45f, 0.45f, 0.4f);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 1. Setup Base Material
        if (routeMaterial == null)
        {
            var shader = Shader.Find("Custom/RouteOverlay");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            routeMaterial = new Material(shader);
        }

        // --- REVERTED: Removed the hardcoded Queue 4000 hack. ---
        // Since Roads are now at Queue 2050 (Geometry+50), 
        // this material (likely 3000 or 4000) will naturally draw on top without hacks.
        
        int baseQueue = routeMaterial.renderQueue;

        // 2. Setup Marker Materials (Stacked relative to the Line)
        _markerMatOuter = new Material(routeMaterial);
        _markerMatOuter.renderQueue = baseQueue + 1; 
        
        _markerMatInner = new Material(routeMaterial);
        _markerMatInner.renderQueue = baseQueue + 2; 
        _markerMatInner.SetVector("_Offset", new Vector4(-10, -10, 0, 0)); 
    }

    public void ShowAll()
    {
        if (TransportManager.Instance == null) return;
        foreach (var route in TransportManager.Instance.ActiveRoutes) ShowRoute(route.RouteID);
    }

    public void HideAll()
    {
        foreach (var lr in _activeLines.Values) if (lr) Destroy(lr.gameObject);
        _activeLines.Clear();
        _originalColors.Clear();
        _highlightedRouteID = null;
    }

    public void ToggleRouteVisibility(string routeID)
    {
        if (_activeLines.ContainsKey(routeID)) HideRoute(routeID);
        else ShowRoute(routeID);
    }

    public void ShowRoute(string routeID)
    {
        Route route = TransportManager.Instance.GetRoute(routeID);
        if (route == null) return;
        HideRoute(routeID);

        GameObject go = new GameObject($"Route_{route.RouteName}");
        go.transform.SetParent(transform);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.alignment = LineAlignment.View; 
        lr.textureMode = LineTextureMode.Tile;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.material = routeMaterial; 
        lr.startColor = route.RouteColor;
        lr.endColor = route.RouteColor;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;

        List<Vector3> rawPoints = new List<Vector3>();
        HashSet<string> markedStops = new HashSet<string>(); 

        int routeIndex = TransportManager.Instance.ActiveRoutes.IndexOf(route);
        if (routeIndex == -1) routeIndex = 0; 
        float myHeight = baseHeight + (routeIndex * heightStep);

        for (int i = 0; i < route.StopIDs.Count - 1; i++)
        {
            BusStop start = TransportManager.Instance.GetStop(route.StopIDs[i]);
            BusStop end = TransportManager.Instance.GetStop(route.StopIDs[i + 1]);
            
            if (start == null || end == null) continue;

            // Start Point & Marker
            AddPoint(rawPoints, start.transform.position, myHeight);
            if (!markedStops.Contains(start.stopID))
            {
                CreateStopMarker(go.transform, start.transform.position, myHeight, route.RouteColor);
                markedStops.Add(start.stopID);
            }

            // Path Nodes
            List<RoadNode> path = TransportManager.Instance.GetPath(start, end);
            if (path != null)
            {
                foreach (var node in path)
                {
                    AddPoint(rawPoints, node.transform.position, myHeight);
                }
            }
        }

        // Final Stop
        if (route.StopIDs.Count > 0)
        {
            BusStop last = TransportManager.Instance.GetStop(route.StopIDs.Last());
            if (last != null)
            {
                AddPoint(rawPoints, last.transform.position, myHeight);
                if (!markedStops.Contains(last.stopID))
                {
                    CreateStopMarker(go.transform, last.transform.position, myHeight, route.RouteColor);
                    markedStops.Add(last.stopID);
                }
            }
        }

        List<Vector3> smoothPoints = GenerateFilletedPath(rawPoints);

        if (smoothPoints.Count > 1)
        {
            lr.positionCount = smoothPoints.Count;
            lr.SetPositions(smoothPoints.ToArray());
            _activeLines[routeID] = lr;
            _originalColors[routeID] = route.RouteColor;
        }
        else
        {
            Destroy(go);
        }
    }

    private void CreateStopMarker(Transform parent, Vector3 pos, float height, Color color)
    {
        Vector3 worldPos = pos;
        worldPos.y += height;

        // 1. Outer Sphere (Queue + 1)
        GameObject outer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(outer.GetComponent<Collider>()); 
        outer.name = "Marker_Outer";
        outer.transform.SetParent(parent);
        outer.transform.position = worldPos;
        outer.transform.localScale = Vector3.one * markerSize;

        MeshRenderer outerRen = outer.GetComponent<MeshRenderer>();
        outerRen.material = _markerMatOuter; 
        outerRen.material.color = color; 
        outerRen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 2. Inner Sphere (Queue + 2)
        GameObject inner = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(inner.GetComponent<Collider>());
        inner.name = "Marker_Inner";
        inner.transform.SetParent(outer.transform);
        inner.transform.localPosition = Vector3.zero;
        inner.transform.localScale = Vector3.one * innerMarkerRatio; 

        MeshRenderer innerRen = inner.GetComponent<MeshRenderer>();
        innerRen.material = _markerMatInner; 
        innerRen.material.color = Color.white;
        innerRen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void AddPoint(List<Vector3> points, Vector3 pos, float height)
    {
        Vector3 finalPos = pos;
        finalPos.y += height;
        if (points.Count > 0 && Vector3.Distance(points.Last(), finalPos) < 0.1f) return;
        points.Add(finalPos);
    }

    private List<Vector3> GenerateFilletedPath(List<Vector3> nodes)
    {
        if (nodes.Count < 3) return nodes;

        List<Vector3> finalPath = new List<Vector3>();
        finalPath.Add(nodes[0]);

        for (int i = 1; i < nodes.Count - 1; i++)
        {
            Vector3 prev = nodes[i - 1];
            Vector3 current = nodes[i];
            Vector3 next = nodes[i + 1];

            Vector3 dirToPrev = (prev - current).normalized;
            Vector3 dirToNext = (next - current).normalized;

            float distToPrev = Vector3.Distance(prev, current);
            float distToNext = Vector3.Distance(next, current);
            
            float actualRadius = Mathf.Min(cornerRadius, distToPrev * 0.45f, distToNext * 0.45f);

            Vector3 startCurve = current + dirToPrev * actualRadius;
            Vector3 endCurve = current + dirToNext * actualRadius;

            finalPath.Add(startCurve);

            for (int j = 1; j <= cornerResolution; j++)
            {
                float t = (float)j / (cornerResolution + 1);
                Vector3 curvePoint = GetQuadraticBezier(startCurve, current, endCurve, t);
                finalPath.Add(curvePoint);
            }
            finalPath.Add(endCurve);
        }
        finalPath.Add(nodes.Last());
        return finalPath;
    }

    private Vector3 GetQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        return (uu * p0) + (2 * u * t * p1) + (tt * p2);
    }

    public void HideRoute(string routeID)
    {
        if (_activeLines.TryGetValue(routeID, out LineRenderer lr))
        {
            if (lr != null) Destroy(lr.gameObject);
            _activeLines.Remove(routeID);
            _originalColors.Remove(routeID);
        }
    }

    // --- UI Toggle Helper ---
    public void SetRoutesVisible(bool isVisible)
    {
        if (isVisible) 
            ShowAll();
        else 
            HideAll();
    }

    // --- Route Highlighting ---

    /// <summary>
    /// Highlights a single route with its original color and greys out all others.
    /// Pass null to clear highlighting and restore all original colors.
    /// </summary>
    public void HighlightRoute(string routeID)
    {
        _highlightedRouteID = routeID;

        if (string.IsNullOrEmpty(routeID))
        {
            // Restore all original colors
            foreach (var kvp in _activeLines)
            {
                if (kvp.Value == null) continue;
                if (_originalColors.TryGetValue(kvp.Key, out Color original))
                    SetLineColor(kvp.Value, original);
            }
            // Restore marker colors
            foreach (var kvp in _activeLines)
            {
                if (kvp.Value == null) continue;
                if (_originalColors.TryGetValue(kvp.Key, out Color original))
                    SetMarkerColors(kvp.Value.transform, original);
            }
            return;
        }

        foreach (var kvp in _activeLines)
        {
            if (kvp.Value == null) continue;
            if (kvp.Key == routeID)
            {
                if (_originalColors.TryGetValue(kvp.Key, out Color original))
                {
                    SetLineColor(kvp.Value, original);
                    SetMarkerColors(kvp.Value.transform, original);
                }
            }
            else
            {
                SetLineColor(kvp.Value, GreyedOutColor);
                SetMarkerColors(kvp.Value.transform, GreyedOutColor);
            }
        }
    }

    /// <summary>
    /// Shows only the specified route, hiding all others.
    /// </summary>
    public void ShowOnlyRoute(string routeID)
    {
        HideAll();
        ShowRoute(routeID);
    }

    private void SetLineColor(LineRenderer lr, Color color)
    {
        lr.startColor = color;
        lr.endColor = color;
    }

    private void SetMarkerColors(Transform routeRoot, Color outerColor)
    {
        foreach (Transform child in routeRoot)
        {
            if (!child.name.StartsWith("Marker_Outer")) continue;
            var outerRen = child.GetComponent<MeshRenderer>();
            if (outerRen != null) outerRen.material.color = outerColor;
        }
    }

    /// <summary>
    /// Returns the number of buses currently assigned to a route.
    /// </summary>
    public static int GetBusCountForRoute(string routeID)
    {
        if (FleetManager.Instance == null) return 0;
        int count = 0;
        foreach (var bus in FleetManager.Instance.allBuses)
        {
            if (bus.Schedule != null && bus.Schedule.RouteID == routeID)
                count++;
        }
        return count;
    }
}