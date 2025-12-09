using UnityEngine;
using UnityEditor;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;

[InitializeOnLoad]
public class BusStopPlacer
{
    static bool isPlacingMode = false;
    const float GRID_SIZE = 50f; 
    static Dictionary<Vector2Int, List<RoadSegment>> spatialGrid = new Dictionary<Vector2Int, List<RoadSegment>>();
    static List<RoadSegment> nearbySegmentsBuffer = new List<RoadSegment>();
    static RoadSegment bestSegment = null;
    static float bestT = 0f;
    static Vector3 bestPos = Vector3.zero;
    static bool hasValidHit = false;

    static BusStopPlacer()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    [MenuItem("Tools/Toggle Bus Stop Placer (P)")]
    public static void ToggleMode()
    {
        isPlacingMode = !isPlacingMode;
        if (isPlacingMode)
        {
            BuildSpatialGrid();
            Selection.activeGameObject = null; 
        }
        Debug.Log(isPlacingMode ? "Bus Stop Placer: <color=green>ENABLED</color>" : "Bus Stop Placer: <color=red>DISABLED</color>");
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/Refresh Road Cache")]
    public static void BuildSpatialGrid()
    {
        spatialGrid.Clear();
        var allSegments = Object.FindObjectsByType<RoadSegment>(FindObjectsSortMode.None);

        foreach (var seg in allSegments)
        {
            var container = seg.GetComponent<SplineContainer>();
            if (container == null || container.Spline == null) continue;

            // Calculate approximate bounds of the spline in World Space
            Bounds bounds = new Bounds(seg.transform.position, Vector3.zero);
            foreach (var knot in container.Spline.Knots)
            {
                // Convert local knot to world
                Vector3 worldKnot = container.transform.TransformPoint(knot.Position);
                bounds.Encapsulate(worldKnot);
            }
            // Expand slightly to account for curve/width
            bounds.Expand(10f); 

            // Add to all overlapping grid cells
            int minX = Mathf.FloorToInt(bounds.min.x / GRID_SIZE);
            int maxX = Mathf.FloorToInt(bounds.max.x / GRID_SIZE);
            int minZ = Mathf.FloorToInt(bounds.min.z / GRID_SIZE);
            int maxZ = Mathf.FloorToInt(bounds.max.z / GRID_SIZE);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    Vector2Int key = new Vector2Int(x, z);
                    if (!spatialGrid.ContainsKey(key))
                        spatialGrid[key] = new List<RoadSegment>();
                    
                    spatialGrid[key].Add(seg);
                }
            }
        }
        Debug.Log($"Bus Stop Placer: Indexed {allSegments.Length} roads into {spatialGrid.Count} grid cells.");
    }

    static void OnSceneGUI(SceneView sceneView)
    {
        if (!isPlacingMode) return;

        Event e = Event.current;
        int controlID = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlID);

        // Raycast to Ground Plane
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Vector3 mouseWorldPos = Vector3.zero;
        bool hitGround = false;

        if (Physics.Raycast(ray, out RaycastHit hit, 5000f))
        {
            mouseWorldPos = hit.point;
            hitGround = true;
        }
        else
        {
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float enter))
            {
                mouseWorldPos = ray.GetPoint(enter);
                hitGround = true;
            }
        }

        // Calculate on MouseMove or Drag
        if (hitGround && (e.type == EventType.MouseMove || e.type == EventType.MouseDrag))
        {
            CalculatePreviewFast(mouseWorldPos);
        }

        DrawHandles(mouseWorldPos, hitGround);
        DrawGUI();

        // Handle Click
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && hasValidHit)
        {
            SpawnStop(bestSegment, bestT);
            e.Use();
        }

        // Force repaint only if mouse is moving
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            sceneView.Repaint();
    }

    static void CalculatePreviewFast(Vector3 searchPoint)
    {
        if (spatialGrid.Count == 0) BuildSpatialGrid();

        bestSegment = null;
        float closestDistSqr = 50f * 50f; // Max snap distance (50m) squared
        hasValidHit = false;

        // Get Grid Cell of Mouse
        int cellX = Mathf.FloorToInt(searchPoint.x / GRID_SIZE);
        int cellZ = Mathf.FloorToInt(searchPoint.z / GRID_SIZE);

        nearbySegmentsBuffer.Clear();

        // Check Mouse Cell + Neighbors (3x3 area)
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                Vector2Int key = new Vector2Int(cellX + x, cellZ + z);
                if (spatialGrid.TryGetValue(key, out var segments))
                {
                    nearbySegmentsBuffer.AddRange(segments);
                }
            }
        }

        // Find Closest in reduced set
        foreach (var seg in nearbySegmentsBuffer)
        {
            if (seg == null) continue;

            SplineContainer container = seg.GetComponent<SplineContainer>();
            if (container == null) continue;

            float3 localPoint = container.transform.InverseTransformPoint(searchPoint);
            
            SplineUtility.GetNearestPoint(
                container.Spline, 
                localPoint, 
                out float3 nearestLocal, 
                out float t
            );

            Vector3 nearestWorld = container.transform.TransformPoint(nearestLocal);
            float dSqr = (searchPoint - nearestWorld).sqrMagnitude;

            if (dSqr < closestDistSqr)
            {
                closestDistSqr = dSqr;
                bestSegment = seg;
                bestT = t;
                bestPos = nearestWorld;
            }
        }

        hasValidHit = (bestSegment != null);
    }

    static void DrawHandles(Vector3 mousePos, bool hitGround)
    {
        if (!hitGround) return;
        
        Handles.color = new Color(1, 1, 1, 0.2f);
        Handles.DrawWireDisc(mousePos, Vector3.up, 0.5f);

        if (hasValidHit)
        {
            Handles.color = Color.green;
            Handles.DrawLine(mousePos, bestPos);
            Handles.DrawSolidDisc(bestPos, Vector3.up, 1.0f);
            Handles.Label(bestPos + Vector3.up * 2f, $"Stop on: {bestSegment.name}");
        }
    }

    static void DrawGUI()
    {
        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(10, 10, 220, 90), EditorStyles.helpBox);
        GUILayout.Label("BUS STOP PLACER (Fast)", EditorStyles.boldLabel);
        GUILayout.Label($"Indexed Cells: {spatialGrid.Count}");
        if (GUILayout.Button("Rebuild Index")) BuildSpatialGrid();
        GUILayout.Label("Left Click to Place");
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    static void SpawnStop(RoadSegment segment, float t)
    {
        GameObject stopObj = new GameObject($"BusStop_{segment.name}");
        Undo.RegisterCreatedObjectUndo(stopObj, "Place Bus Stop");

        stopObj.transform.parent = segment.transform;

        BusStop stop = stopObj.AddComponent<BusStop>();
        stop.parentSegment = segment;
        stop.splineT = t;
        stop.stopID = System.Guid.NewGuid().ToString().Substring(0, 8);

        stop.SnapToSegment();
        Debug.Log($"Placed Stop on {segment.name} at T: {t:F3}");
    }
}