using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; // For GUI text styling

public class SelectionBoxController : MonoBehaviour
{
    [Header("References")]
    public List<Transform> terrains = new List<Transform>();
    public LayerMask terrainLayerMask = -1;
    public RTSCameraController cameraController;

    [Header("Selection Settings")]
    public float pointSpacing = 1f;
    public float clickThreshold = 5f;
    public LayerMask selectableLayer;

    [Header("Smoothing Settings")]
    public bool useSmoothing = true;
    public int interpolationSteps = 10;

    public List<GameObject> selectedObjects = new List<GameObject>();
    public float lineOffset = 0.1f;

    private List<GameObject> lastSelectedObjects = new List<GameObject>();

    [Header("2D Selection Box Settings")]
    public Color selectionBoxFillColor = new Color(1, 0, 0, 0.3f);    // Red with 30% opacity
    public Color selectionBoxOutlineColor = Color.red;

    [Header("Object Tracking")]
    public bool autoTrackSingleSelection = false;
    public bool focusOnSelection = true;

    public Color outlineColor = Color.white;

    // --- 3D selection (normal) variables ---
    bool isSelecting = false;
    bool isDragging = false;
    Vector3 startWorldPoint;
    Vector3 currentWorldPoint;
    Vector3 startScreenPoint;
    LineRenderer lineRenderer;

    // 2D selection variables.
    bool is2DSelecting = false;
    Vector2 startScreenPoint2D;
    Vector2 currentScreenPoint2D;

    void Awake()
    {
        if (pointSpacing <= 0f)
            pointSpacing = 1f;

        // Setup our primary line renderer.
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startWidth = 0.5f;
        lineRenderer.endWidth = 0.5f;
        AnimationCurve curve = new AnimationCurve();
        curve.AddKey(0f, 0.5f);
        curve.AddKey(1f, 0.5f);
        lineRenderer.widthCurve = curve;
        lineRenderer.loop = true;
        lineRenderer.positionCount = 0;
        lineRenderer.sortingLayerName = "UI";
        lineRenderer.sortingOrder = 1000;
        lineRenderer.useWorldSpace = true;  // Keep in world space
        lineRenderer.startColor = Color.white;
        lineRenderer.endColor = Color.white;
    }

    void Update()
    {
        if(Input.GetKeyDown(KeyCode.F)){
            Debug.Log("Pressed F key");
        }
        if (Camera.main == null)
            return;

        // Handle F key for focus/track selected objects
        if (Input.GetKeyDown(KeyCode.F) && selectedObjects.Count > 0)
        {
            Debug.Log("pressed F key");
            if (selectedObjects.Count == 1)
            {
                if (autoTrackSingleSelection)
                    cameraController.StartTrackingObject(selectedObjects[0].transform);
                else
                    cameraController.FocusOnObject(selectedObjects[0].transform);
            }
            else
            {
                // Focus on center of multiple selected objects
                FocusOnSelectedObjects();
            }
        }

        HandleNormalSelection();
    }

    #region Normal Selection

    void HandleNormalSelection()
    {
        // Start selection
        if (Input.GetMouseButtonDown(1))
        {
            if (Input.GetKey(KeyCode.LeftAlt))
            {
                is2DSelecting = true;
                startScreenPoint2D = Input.mousePosition;
                isSelecting = false;
                ClearLine();
            }
            else
            {
                isSelecting = true;
                isDragging = false;
                startScreenPoint = Input.mousePosition;

                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                
                // Use terrainLayerMask for raycast to find start point
                if (Physics.Raycast(ray, out hit, 1000f, terrainLayerMask))
                {
                    startWorldPoint = hit.point;
                }
                else
                {
                    // Fallback to plane if no terrain hit
                    Plane plane = new Plane(Vector3.up, Vector3.zero);
                    float distance;
                    startWorldPoint = plane.Raycast(ray, out distance) ? ray.GetPoint(distance) : ray.origin;
                }

                ClearLine();
            }
        }

        // Update selection box while dragging
        if (Input.GetMouseButton(1))
        {
            if (is2DSelecting)
                currentScreenPoint2D = Input.mousePosition;
            else if (isSelecting)
            {
                if (Vector3.Distance(Input.mousePosition, startScreenPoint) > clickThreshold)
                {
                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    RaycastHit hit;
                    
                    // Use terrainLayerMask for raycast during dragging
                    if (Physics.Raycast(ray, out hit, 1000f, terrainLayerMask))
                    {
                        isDragging = true;
                        currentWorldPoint = hit.point;
                        DrawSelectionBox();
                    }
                    else
                    {
                        ClearLine();
                    }
                }
                else
                    ClearLine();
            }
        }

        // End selection
        if (Input.GetMouseButtonUp(1))
        {
            if (is2DSelecting)
            {
                SelectObjectsInScreenRect();
                is2DSelecting = false;
            }
            else if (isSelecting)
            {
                if (isDragging)
                    SelectObjectsInRectangle();
                else
                    SingleSelect();

                isSelecting = false;
                isDragging = false;
                ClearLine();
                UpdateSelectionOutlines();
            }
        }
    }

    #endregion

    #region Selection Box, Outlines & Other Helpers

    void DrawSelectionBox()
    {
        float minX = Mathf.Min(startWorldPoint.x, currentWorldPoint.x);
        float maxX = Mathf.Max(startWorldPoint.x, currentWorldPoint.x);
        float minZ = Mathf.Min(startWorldPoint.z, currentWorldPoint.z);
        float maxZ = Mathf.Max(startWorldPoint.z, currentWorldPoint.z);
        if (Mathf.Approximately(minX, maxX) && Mathf.Approximately(minZ, maxZ))
        {
            ClearLine();
            return;
        }
        Vector3 bottomLeft = new Vector3(minX, GetTerrainHeight(new Vector3(minX, 0, minZ)), minZ);
        Vector3 bottomRight = new Vector3(maxX, GetTerrainHeight(new Vector3(maxX, 0, minZ)), minZ);
        Vector3 topRight = new Vector3(maxX, GetTerrainHeight(new Vector3(maxX, 0, maxZ)), maxZ);
        Vector3 topLeft = new Vector3(minX, GetTerrainHeight(new Vector3(minX, 0, maxZ)), maxZ);

        List<Vector3> points = new List<Vector3>();
        AddEdgePoints(points, bottomLeft, bottomRight);
        AddEdgePoints(points, bottomRight, topRight);
        AddEdgePoints(points, topRight, topLeft);
        AddEdgePoints(points, topLeft, bottomLeft);

        List<Vector3> finalPoints = useSmoothing && points.Count >= 4 ? SmoothCurve(points, interpolationSteps) : points;
        UpdateLineRenderer(finalPoints);
    }

    void UpdateLineRenderer(List<Vector3> positions)
    {
        if (positions == null || positions.Count == 0)
        {
            lineRenderer.positionCount = 0;
            return;
        }
        lineRenderer.positionCount = positions.Count;
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 pos = positions[i];
            pos.y += lineOffset;
            lineRenderer.SetPosition(i, pos);
        }
    }

    void UpdateSelectionOutlines()
    {
        foreach (GameObject obj in lastSelectedObjects)
        {
            if (!selectedObjects.Contains(obj))
            {
                Outline outline = obj.GetComponent<Outline>();
                if (outline != null)
                    outline.enabled = false;
            }
        }
        foreach (GameObject obj in selectedObjects)
        {
            Outline outline = obj.GetComponent<Outline>();
            if (outline == null)
            {
                outline = obj.AddComponent<Outline>();
                outline.OutlineMode = Outline.Mode.OutlineAll;
                outline.OutlineColor = outlineColor;
                outline.OutlineWidth = 5f;
            }
            else
                outline.enabled = true;
        }
        lastSelectedObjects = new List<GameObject>(selectedObjects);
    }

    List<Vector3> SmoothCurve(List<Vector3> points, int steps)
    {
        List<Vector3> smoothed = new List<Vector3>();
        int count = points.Count;
        if (count < 4)
            return new List<Vector3>(points);
        for (int i = 0; i < count; i++)
        {
            Vector3 p0 = points[(i - 1 + count) % count];
            Vector3 p1 = points[i];
            Vector3 p2 = points[(i + 1) % count];
            Vector3 p3 = points[(i + 2) % count];
            for (int j = 0; j < steps; j++)
            {
                float t = j / (float)steps;
                Vector3 newPoint = CatmullRom(p0, p1, p2, p3, t);
                smoothed.Add(newPoint);
            }
        }
        if (smoothed.Count > 0)
            smoothed.Add(smoothed[0]);
        return smoothed;
    }

    Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1) +
                       (-p0 + p2) * t +
                       (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                       (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    void ClearLine()
    {
        if (lineRenderer != null)
            lineRenderer.positionCount = 0;
    }

    bool IsValidVector(Vector3 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                 float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }

    float GetTerrainHeight(Vector3 worldPos)
    {
        // Use efficient raycast to find highest surface on terrain layer
        Vector3 rayStart = new Vector3(worldPos.x, 1000f, worldPos.z);
        Vector3 rayDirection = Vector3.down;
        
        // Cast ray downward to find highest surface
        if (Physics.Raycast(rayStart, rayDirection, out RaycastHit hit, 1500f, terrainLayerMask))
        {
            return hit.point.y;
        }
        
        // Fallback to terrain list method if raycast misses
        return GetTerrainHeightFallback(worldPos);
    }
    
    float GetTerrainHeightFallback(Vector3 worldPos)
    {
        foreach (Transform terrainTransform in terrains)
        {
            if (terrainTransform != null)
            {
                Terrain t = terrainTransform.GetComponent<Terrain>();
                if (t != null)
                {
                    Vector3 terrainPos = terrainTransform.position;
                    Vector3 terrainSize = t.terrainData.size;
                    if (worldPos.x >= terrainPos.x && worldPos.x <= terrainPos.x + terrainSize.x &&
                        worldPos.z >= terrainPos.z && worldPos.z <= terrainPos.z + terrainSize.z)
                    {
                        return t.SampleHeight(worldPos) + terrainTransform.position.y;
                    }
                }
                else
                {
                    // Handle quads or other objects - check if position is within bounds
                    Renderer renderer = terrainTransform.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        Bounds bounds = renderer.bounds;
                        if (bounds.Contains(new Vector3(worldPos.x, bounds.center.y, worldPos.z)))
                        {
                            return terrainTransform.position.y;
                        }
                    }
                }
            }
        }
        return worldPos.y;
    }

    void AddEdgePoints(List<Vector3> points, Vector3 start, Vector3 end)
    {
        float edgeLength = Vector3.Distance(start, end);
        if (edgeLength < 0.001f)
        {
            points.Add(start);
            return;
        }
        int numPoints = Mathf.Max(1, Mathf.CeilToInt(edgeLength / pointSpacing));
        for (int i = 0; i <= numPoints; i++)
        {
            float t = i / (float)numPoints;
            Vector3 pos = Vector3.Lerp(start, end, t);
            pos.y = GetTerrainHeight(new Vector3(pos.x, 0, pos.z));
            if (IsValidVector(pos))
                points.Add(pos);
        }
    }

    void SelectObjectsInRectangle()
    {
        bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        float minX = Mathf.Min(startWorldPoint.x, currentWorldPoint.x);
        float maxX = Mathf.Max(startWorldPoint.x, currentWorldPoint.x);
        float minZ = Mathf.Min(startWorldPoint.z, currentWorldPoint.z);
        float maxZ = Mathf.Max(startWorldPoint.z, currentWorldPoint.z);

        Vector3 center = new Vector3((minX + maxX) / 2f, 0, (minZ + maxZ) / 2f);
        Vector3 halfExtents = new Vector3((maxX - minX) / 2f, 1000, (maxZ - minZ) / 2f);
        Collider[] colliders = Physics.OverlapBox(center, halfExtents, Quaternion.identity, selectableLayer);
        if (!ctrlDown)
            selectedObjects.Clear();
        foreach (Collider col in colliders)
        {
            bool isTerrainObject = false;
            foreach (Transform terrainTransform in terrains)
            {
                if (col.gameObject == terrainTransform.gameObject)
                {
                    isTerrainObject = true;
                    break;
                }
            }
            if (!isTerrainObject && !selectedObjects.Contains(col.gameObject))
                selectedObjects.Add(col.gameObject);
        }
        if (selectedObjects.Count > 0 && focusOnSelection)
        {
            if (selectedObjects.Count == 1 && autoTrackSingleSelection && cameraController != null)
            {
                cameraController.StartTrackingObject(selectedObjects[0].transform);
            }
            else
            {
                FocusOnSelectedObjects();
            }
        }
    }

    void SingleSelect()
    {
        bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 1000f, selectableLayer))
        {
            GameObject obj = hit.collider.gameObject;
            bool isTerrainObject = false;
            foreach (Transform terrainTransform in terrains)
            {
                if (obj == terrainTransform.gameObject)
                {
                    isTerrainObject = true;
                    break;
                }
            }
            if (!isTerrainObject)
            {
                if (ctrlDown)
                {
                    if (selectedObjects.Contains(obj))
                        selectedObjects.Remove(obj);
                    else
                        selectedObjects.Add(obj);
                }
                else
                {
                    selectedObjects.Clear();
                    selectedObjects.Add(obj);
                }
                if (selectedObjects.Count > 0 && focusOnSelection)
                {
                    if (selectedObjects.Count == 1 && autoTrackSingleSelection && cameraController != null)
                    {
                        cameraController.StartTrackingObject(selectedObjects[0].transform);
                    }
                    else
                    {
                        FocusOnSelectedObjects();
                    }
                }
                return;
            }
        }
        if (!ctrlDown)
        {
            selectedObjects.Clear();
            Ray terrainRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit terrainHit;
            bool hitTerrain = false;
            foreach (Transform terrainTransform in terrains)
            {
                if (terrainTransform != null)
                {
                    TerrainCollider tc = terrainTransform.GetComponent<TerrainCollider>();
                    if (tc != null && tc.Raycast(terrainRay, out terrainHit, 1000f))
                    {
                        Vector3 focusPos = terrainHit.point;
                        focusPos.y = GetTerrainHeight(new Vector3(focusPos.x, 0, focusPos.z));
                        if (cameraController != null)
                            cameraController.SetTargetFocusPoint(focusPos);
                        hitTerrain = true;
                        break;
                    }
                    else
                    {
                        Collider collider = terrainTransform.GetComponent<Collider>();
                        if (collider != null && collider.Raycast(terrainRay, out terrainHit, 1000f))
                        {
                            Vector3 focusPos = terrainHit.point;
                            focusPos.y = GetTerrainHeight(new Vector3(focusPos.x, 0, focusPos.z));
                            if (cameraController != null)
                                cameraController.SetTargetFocusPoint(focusPos);
                            hitTerrain = true;
                            break;
                        }
                    }
                }
            }
        }
    }

    void SelectObjectsInScreenRect()
    {
        bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (!ctrlDown)
            selectedObjects.Clear();

        Rect selectionRect = GetScreenRect(startScreenPoint2D, currentScreenPoint2D);
        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
        foreach (Collider col in colliders)
        {
            if ((selectableLayer.value & (1 << col.gameObject.layer)) == 0)
                continue;
            bool isTerrainObject = false;
            foreach (Transform terrainTransform in terrains)
            {
                if (col.gameObject == terrainTransform.gameObject)
                {
                    isTerrainObject = true;
                    break;
                }
            }
            if (isTerrainObject)
                continue;

            Vector3 screenPos = Camera.main.WorldToScreenPoint(col.transform.position);
            Vector2 guiPoint = new Vector2(screenPos.x, Screen.height - screenPos.y);
            if (selectionRect.Contains(guiPoint) && !selectedObjects.Contains(col.gameObject))
                selectedObjects.Add(col.gameObject);
        }

        if (selectedObjects.Count > 0 && focusOnSelection)
        {
            if (selectedObjects.Count == 1 && autoTrackSingleSelection && cameraController != null)
            {
                cameraController.StartTrackingObject(selectedObjects[0].transform);
            }
            else
            {
                FocusOnSelectedObjects();
            }
        }
        UpdateSelectionOutlines();
    }

    void FocusOnSelectedObjects()
    {
        if (selectedObjects.Count > 0 && cameraController != null)
        {
            Vector3 sum = Vector3.zero;
            foreach (GameObject obj in selectedObjects)
                sum += new Vector3(obj.transform.position.x, 0, obj.transform.position.z);
            Vector3 avg = sum / selectedObjects.Count;
            float y = GetTerrainHeight(new Vector3(avg.x, 0, avg.z));
            Vector3 targetFocus = new Vector3(avg.x, y, avg.z);
            cameraController.SetTargetFocusPoint(targetFocus);
        }
    }

    Rect GetScreenRect(Vector2 screenPosition1, Vector2 screenPosition2)
    {
        screenPosition1.y = Screen.height - screenPosition1.y;
        screenPosition2.y = Screen.height - screenPosition2.y;
        Vector2 topLeft = Vector2.Min(screenPosition1, screenPosition2);
        Vector2 bottomRight = Vector2.Max(screenPosition1, screenPosition2);
        return new Rect(topLeft.x, topLeft.y, bottomRight.x - topLeft.x, bottomRight.y - topLeft.y);
    }

    void OnGUI()
    {
        // Draw 2D selection box if active.
        if (is2DSelecting)
        {
            Rect rect = GetScreenRect(startScreenPoint2D, Input.mousePosition);
            Color prevColor = GUI.color;
            GUI.color = selectionBoxFillColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = selectionBoxOutlineColor;
            GUI.Box(rect, "");
            GUI.color = prevColor;
        }
    }

    #endregion
}