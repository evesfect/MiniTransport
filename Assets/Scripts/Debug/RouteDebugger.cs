using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class RouteDebugger : MonoBehaviour
{
    [Header("Route Manager")]
    [Tooltip("Enter the name of the route you want to Create, Edit, or Delete.")]
    public string targetRouteName = "Route 1";
    public Color routeColor = Color.cyan;
    
    [Header("Stop Editor")]
    [Tooltip("Drag stops here to define the path. Click 'Load Route' to fill this from an existing route.")]
    public List<BusStop> editorStops = new List<BusStop>();

    [Header("Visualization Settings")]
    public float baseHeight = 5f;
    [Tooltip("Height added per route index to avoid overlapping lines.")]
    public float heightStep = 2f; 
    public float lineWidth = 1f;

    private List<GameObject> _spawnedVisuals = new List<GameObject>();

    // --- 1. CREATE / UPDATE ---
    [ContextMenu("1. Create / Update Route")]
    public void CreateOrUpdateRoute()
    {
        if (TransportManager.Instance == null)
        {
            Debug.LogError("TransportManager missing!");
            return;
        }

        // 1. Validate Input
        List<string> ids = new List<string>();
        foreach (var stop in editorStops)
        {
            if (stop != null) ids.Add(stop.stopID);
        }

        if (ids.Count < 2)
        {
            Debug.LogWarning("Need at least 2 stops to define a route.");
            return;
        }

        // 2. Check if exists
        Route existingRoute = TransportManager.Instance.ActiveRoutes.FirstOrDefault(r => r.RouteName == targetRouteName);

        if (existingRoute != null)
        {
            // UPDATE
            existingRoute.StopIDs = new List<string>(ids);
            existingRoute.RouteColor = routeColor;
            
            // Re-calculate the path immediately
            TransportManager.Instance.CacheRoute(existingRoute);
            
            Debug.Log($"Updated existing route: {existingRoute.RouteID}");
        }
        else
        {
            // CREATE
            Route newRoute = TransportManager.Instance.CreateRoute(targetRouteName, ids, routeColor);
            
            // Ensure path is calculated immediately
            TransportManager.Instance.CacheRoute(newRoute);
            
            Debug.Log($"Created New Route: '{targetRouteName}'");
        }

        // 3. Save & Refresh
        TransportManager.Instance.SaveRoutes();
        VisualizeAllRoutes();
    }

    // --- 2. LOAD (Edit Mode) ---
    [ContextMenu("2. Load Route to Editor")]
    public void LoadRouteToEditor()
    {
        if (TransportManager.Instance == null) return;

        Route r = TransportManager.Instance.ActiveRoutes.FirstOrDefault(r => r.RouteName == targetRouteName);
        if (r == null)
        {
            Debug.LogWarning($"Route '{targetRouteName}' not found.");
            return;
        }

        // Fill Inspector List
        editorStops.Clear();
        foreach (string id in r.StopIDs)
        {
            BusStop stop = TransportManager.Instance.GetStop(id);
            if (stop != null) editorStops.Add(stop);
            else Debug.LogWarning($"Stop ID {id} in route '{targetRouteName}' not found in scene.");
        }

        // Sync settings
        routeColor = r.RouteColor;
        
        Debug.Log($"Loaded '{targetRouteName}' into Inspector.");
    }

    // --- 3. DELETE ---
    [ContextMenu("3. Delete Route")]
    public void DeleteRoute()
    {
        if (TransportManager.Instance == null) return;

        Route r = TransportManager.Instance.ActiveRoutes.FirstOrDefault(r => r.RouteName == targetRouteName);
        if (r != null)
        {
            TransportManager.Instance.DeleteRoute(r);
            Debug.Log($"Deleted Route: '{targetRouteName}'");
            VisualizeAllRoutes(); 
        }
        else
        {
            Debug.LogWarning($"Cannot delete: Route '{targetRouteName}' not found.");
        }
    }

    // --- 4. VISUALIZE ALL ---
    [ContextMenu("4. Visualize All Routes")]
    public void VisualizeAllRoutes()
    {
        foreach (var obj in _spawnedVisuals)
        {
            if (obj != null) DestroyImmediate(obj);
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
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = route.RouteColor;
        lr.endColor = route.RouteColor;

        List<Vector3> points = new List<Vector3>();

        for (int i = 0; i < route.StopIDs.Count - 1; i++)
        {
            BusStop start = TransportManager.Instance.GetStop(route.StopIDs[i]);
            BusStop end = TransportManager.Instance.GetStop(route.StopIDs[i + 1]);

            if (start == null || end == null) continue;

            // 1. Add Start Stop
            points.Add(start.transform.position + Vector3.up * heightOffset);

            // 2. Add Path Nodes (from Cache)
            List<RoadNode> path = TransportManager.Instance.GetCachedPath(start, end);
            if (path != null)
            {
                foreach (var node in path)
                {
                    points.Add(node.transform.position + Vector3.up * heightOffset);
                }
            }
        }

        // 3. Add Final Stop
        if (route.StopIDs.Count > 0)
        {
            BusStop last = TransportManager.Instance.GetStop(route.StopIDs.Last());
            if (last != null) points.Add(last.transform.position + Vector3.up * heightOffset);
        }

        lr.positionCount = points.Count;
        lr.SetPositions(points.ToArray());
    }
}