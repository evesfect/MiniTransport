using UnityEngine;
using UnityEditor;
using UnityEngine.Splines;
using UnityEditorInternal;
using System.Collections.Generic;
using Unity.Mathematics;

public class MassBuildingPlacer : EditorWindow
{
    public GameObject buildingPrefab;
    public string parentObjectName = "Buildings";
    
    public int terrainLayer; 
    public LayerMask obstacleMask; 
    public int buildingTargetLayer;

    public float stepDistance = 25f;
    public float curbOffset = 10f;
    public float roadClearanceThreshold = 7f;
    public float raycastHeight = 2000f; 

    public int previewLimit = 2000;

    // SCALE RANDOMIZATION
    public bool randomizeScale = true;
    public bool uniformScale = true;
    public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
    public Vector2 scaleRangeX = new Vector2(0.8f, 1.2f);
    public Vector2 scaleRangeY = new Vector2(0.8f, 1.2f);
    public Vector2 scaleRangeZ = new Vector2(0.8f, 1.2f);

    private const string GENERATED_NAME = "Generated_Building";
    
    private List<PreviewInstance> cachedPreview = new List<PreviewInstance>();
    private List<RoadData> cachedRoads = new List<RoadData>();
    private HashSet<Vector2Int> occupiedGrid = new HashSet<Vector2Int>();
    private float gridSize;

    private struct RoadData {
        public SplineContainer container;
        public Bounds worldBounds;
    }

    private struct PreviewInstance {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 size;
        public Vector3 scale;
    }

    // PREFS
    private const string KEY_PREFAB = "MBP_PrefabPath";
    private const string KEY_PARENT = "MBP_ParentName";
    private const string KEY_TERRAIN = "MBP_TerrainLayer";
    private const string KEY_OBSTACLE = "MBP_ObstacleMask";
    private const string KEY_TARGET = "MBP_TargetLayer";
    private const string KEY_STEP = "MBP_StepDist";
    private const string KEY_CURB = "MBP_CurbOffset";
    private const string KEY_CLEARANCE = "MBP_Clearance";
    private const string KEY_RAYHEIGHT = "MBP_RayHeight";
    private const string KEY_LIMIT = "MBP_PreviewLimit";
    private const string KEY_RANDSCALE = "MBP_RandomizeScale";
    private const string KEY_UNIFORMSCALE = "MBP_UniformScale";
    private const string KEY_SCALEMIN = "MBP_ScaleMin";
    private const string KEY_SCALEMAX = "MBP_ScaleMax";
    private const string KEY_SCALEXMIN = "MBP_ScaleXMin";
    private const string KEY_SCALEXMAX = "MBP_ScaleXMax";
    private const string KEY_SCALEYMIN = "MBP_ScaleYMin";
    private const string KEY_SCALEYMAX = "MBP_ScaleYMax";
    private const string KEY_SCALEZMIN = "MBP_ScaleZMin";
    private const string KEY_SCALEZMAX = "MBP_ScaleZMax";

    [MenuItem("Tools/Mass Building Placer")]
    public static void ShowWindow() => GetWindow<MassBuildingPlacer>("Building Placer");

    private void OnEnable() {
        SceneView.duringSceneGui += OnSceneGUI;
        LoadSettings();
    }
    private void OnDisable() {
        SceneView.duringSceneGui -= OnSceneGUI;
        SaveSettings();
    }

    private void OnGUI() {
        GUILayout.Label("High-Performance Building Placer", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();

        buildingPrefab = (GameObject)EditorGUILayout.ObjectField("Building Prefab", buildingPrefab, typeof(GameObject), false);
        parentObjectName = EditorGUILayout.TextField("Parent Name", parentObjectName);

        terrainLayer = EditorGUILayout.LayerField("Terrain Layer", terrainLayer);

        // Custom Layer Mask Drawer
        string[] allLayers = new string[32];
        for (int i = 0; i < 32; i++) {
            string name = LayerMask.LayerToName(i);
            allLayers[i] = string.IsNullOrEmpty(name) ? "" : name; 
        }
        obstacleMask = EditorGUILayout.MaskField("Obstacle Mask", obstacleMask, allLayers);

        buildingTargetLayer = EditorGUILayout.LayerField("Spawned Building Layer", buildingTargetLayer);

        EditorGUILayout.Space();
        stepDistance = Mathf.Max(2f, EditorGUILayout.FloatField("Step Distance (m)", stepDistance));
        curbOffset = EditorGUILayout.FloatField("Curb Offset (m)", curbOffset);
        roadClearanceThreshold = EditorGUILayout.FloatField("Road Clearance (m)", roadClearanceThreshold);
        raycastHeight = EditorGUILayout.FloatField("Raycast Height (m)", raycastHeight);
        
        previewLimit = EditorGUILayout.IntSlider("Preview Limit", previewLimit, 0, 10000);

        EditorGUILayout.Space();
        randomizeScale = EditorGUILayout.Toggle("Randomize Scale", randomizeScale);
        if (randomizeScale) {
            EditorGUI.indentLevel++;
            uniformScale = EditorGUILayout.Toggle("Uniform Scale", uniformScale);
            if (uniformScale) {
                scaleRange = DrawMinMax("Scale Range", scaleRange);
            } else {
                scaleRangeX = DrawMinMax("Scale X", scaleRangeX);
                scaleRangeY = DrawMinMax("Scale Y", scaleRangeY);
                scaleRangeZ = DrawMinMax("Scale Z", scaleRangeZ);
            }
            EditorGUI.indentLevel--;
        }

        if (EditorGUI.EndChangeCheck()) SaveSettings();

        EditorGUILayout.Space(10);
        
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1. GENERATE PREVIEW", GUILayout.Height(30))) CalculatePreview();
        if (GUILayout.Button("CLEAR", GUILayout.Height(30), GUILayout.Width(60))) {
            cachedPreview.Clear();
            SceneView.RepaintAll();
        }
        GUILayout.EndHorizontal();

        GUI.enabled = cachedPreview.Count > 0; 
        if (GUILayout.Button($"2. SPAWN {cachedPreview.Count} BUILDINGS", GUILayout.Height(40))) ExecuteSpawnFromPreview();
        GUI.enabled = true;

        if (GUILayout.Button("CLEANUP ALL", GUILayout.Height(20))) CleanupBuildings();
    }

    private Vector2 DrawMinMax(string label, Vector2 range) {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);
        float min = EditorGUILayout.FloatField(range.x, GUILayout.MinWidth(40));
        float max = EditorGUILayout.FloatField(range.y, GUILayout.MinWidth(40));
        EditorGUILayout.EndHorizontal();
        min = Mathf.Max(0.01f, min);
        max = Mathf.Max(min, max);
        return new Vector2(min, max);
    }

    private Vector3 GenerateRandomScale() {
        if (!randomizeScale) return Vector3.one;
        if (uniformScale) {
            float s = UnityEngine.Random.Range(scaleRange.x, scaleRange.y);
            return new Vector3(s, s, s);
        }
        return new Vector3(
            UnityEngine.Random.Range(scaleRangeX.x, scaleRangeX.y),
            UnityEngine.Random.Range(scaleRangeY.x, scaleRangeY.y),
            UnityEngine.Random.Range(scaleRangeZ.x, scaleRangeZ.y)
        );
    }

    private void CalculatePreview() {
        CacheRoads();
        cachedPreview.Clear();
        occupiedGrid.Clear(); 

        if (buildingPrefab == null) {
            Debug.LogError("Assign a building prefab first!");
            return;
        }

        Physics.SyncTransforms();

        Vector3 bSize = GetPrefabSize(buildingPrefab);
        gridSize = Mathf.Min(bSize.x, bSize.z) * 0.5f;
        if (gridSize < 1f) gridSize = 1f;

        int calculatedCount = 0;

        foreach (var road in cachedRoads) {
            foreach (var spline in road.container.Splines) {
                float len = spline.GetLength();
                for (float d = 0; d < len; d += stepDistance) {
                    if (calculatedCount >= previewLimit) break;

                    float t = d / len;
                    Vector3 wPos = road.container.transform.TransformPoint((Vector3)spline.EvaluatePosition(t));
                    Vector3 wTan = road.container.transform.TransformDirection((Vector3)spline.EvaluateTangent(t));

                    Vector3 scaleR = GenerateRandomScale();
                    if (TryCalculatePoint(wPos, wTan, true, Vector3.Scale(bSize, scaleR), scaleR, out PreviewInstance p1)) {
                        cachedPreview.Add(p1);
                        calculatedCount++;
                    }

                    Vector3 scaleL = GenerateRandomScale();
                    if (calculatedCount < previewLimit && TryCalculatePoint(wPos, wTan, false, Vector3.Scale(bSize, scaleL), scaleL, out PreviewInstance p2)) {
                        cachedPreview.Add(p2);
                        calculatedCount++;
                    }
                }
            }
        }
        
        SceneView.RepaintAll();
        Debug.Log($"Calculated {cachedPreview.Count} points.");
    }

    private bool TryCalculatePoint(Vector3 roadPos, Vector3 tangent, bool isRight, Vector3 bSize, Vector3 scale, out PreviewInstance result) {
        result = default;
        
        Vector3 sideDir = Vector3.Cross(Vector3.up, tangent).normalized;
        if (!isRight) sideDir = -sideDir;

        Vector3 targetPos = roadPos + (sideDir * (curbOffset + bSize.z * 0.5f));
        Vector3 rayOrigin = targetPos + Vector3.up * raycastHeight;

        // 1. Find Ground
        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit terrainHit, raycastHeight * 2f, 1 << terrainLayer))
            return false;

        // 2. Check Obstacles
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit obsHit, raycastHeight * 2f, obstacleMask)) {
            float difference = terrainHit.distance - obsHit.distance;
            if (difference > -0.5f) return false; 
        }

        Vector3 finalPos = terrainHit.point;

        // 3. Spatial Hash
        Vector2Int gridCoord = new Vector2Int(
            Mathf.RoundToInt(finalPos.x / gridSize), 
            Mathf.RoundToInt(finalPos.z / gridSize)
        );

        if (IsGridOccupied(gridCoord)) return false;

        // 4. Road Check
        foreach (var road in cachedRoads) {
            if (!road.worldBounds.Contains(finalPos)) continue;
            foreach (var s in road.container.Splines) {
                float3 localPoint = road.container.transform.InverseTransformPoint(finalPos);
                SplineUtility.GetNearestPoint(s, localPoint, out float3 nearestLocal, out float t);
                float dist = Vector3.Distance(finalPos, road.container.transform.TransformPoint(nearestLocal));
                if (dist < roadClearanceThreshold) return false;
            }
        }

        // 5. Final Box Check
        Quaternion rotation = Quaternion.LookRotation(-sideDir, Vector3.up);
        Vector3 boxCenter = finalPos + Vector3.up * (bSize.y * 0.5f);
        if (Physics.CheckBox(boxCenter, bSize * 0.45f, rotation, obstacleMask)) return false;

        RegisterGridOccupancy(gridCoord);
        result = new PreviewInstance { position = finalPos, rotation = rotation, size = bSize, scale = scale };
        return true;
    }

    private bool IsGridOccupied(Vector2Int center) {
        for (int x = -1; x <= 1; x++) {
            for (int y = -1; y <= 1; y++) {
                if (occupiedGrid.Contains(center + new Vector2Int(x, y))) return true;
            }
        }
        return false;
    }

    private void RegisterGridOccupancy(Vector2Int center) {
        occupiedGrid.Add(center);
    }

    private void OnSceneGUI(SceneView sceneView) {
        if (cachedPreview.Count == 0) return;
        Handles.color = new Color(0, 1, 1, 0.5f);
        foreach (var p in cachedPreview) {
            Handles.matrix = Matrix4x4.TRS(p.position + Vector3.up * (p.size.y * 0.5f), p.rotation, Vector3.one);
            Handles.DrawWireCube(Vector3.zero, p.size);
        }
        Handles.matrix = Matrix4x4.identity;
    }

    private void ExecuteSpawnFromPreview() {
        if (cachedPreview.Count == 0) return;
        GameObject parent = GameObject.Find(parentObjectName) ?? new GameObject(parentObjectName);
        
        // Define valid static flags (Removing Deprecated Navigation flags)
        StaticEditorFlags validFlags = 
            StaticEditorFlags.ContributeGI | 
            StaticEditorFlags.OccluderStatic | 
            StaticEditorFlags.BatchingStatic | 
            StaticEditorFlags.ReflectionProbeStatic;

        foreach (var p in cachedPreview) {
            GameObject house = (GameObject)PrefabUtility.InstantiatePrefab(buildingPrefab);
            house.transform.position = p.position;
            house.transform.rotation = p.rotation;
            house.transform.localScale = Vector3.Scale(house.transform.localScale, p.scale);
            house.transform.SetParent(parent.transform, true);
            house.name = GENERATED_NAME;
            
            // Set Layer
            SetLayerRecursively(house, buildingTargetLayer);
            
            // Set Static Flags
            GameObjectUtility.SetStaticEditorFlags(house, validFlags);

            // Also ensure all children are marked static
            foreach(Transform child in house.GetComponentsInChildren<Transform>(true)) {
                 GameObjectUtility.SetStaticEditorFlags(child.gameObject, validFlags);
            }

            Undo.RegisterCreatedObjectUndo(house, "Spawn Building");
        }
        
        Debug.Log($"Spawned {cachedPreview.Count} static buildings.");
        cachedPreview.Clear();
        occupiedGrid.Clear();
        SceneView.RepaintAll();
    }

    private void SetLayerRecursively(GameObject obj, int layer) {
        obj.layer = layer;
        foreach (Transform child in obj.transform) SetLayerRecursively(child.gameObject, layer);
    }

    private void CacheRoads() {
        cachedRoads.Clear();
        var containers = Object.FindObjectsByType<SplineContainer>(FindObjectsSortMode.None);
        foreach (var c in containers) {
            Bounds b = new Bounds(c.transform.position, Vector3.zero);
            foreach(var s in c.Splines) {
                var localBounds = s.GetBounds();
                Vector3 worldMin = c.transform.TransformPoint(localBounds.min);
                Vector3 worldMax = c.transform.TransformPoint(localBounds.max);
                b.Encapsulate(worldMin);
                b.Encapsulate(worldMax);
            }
            b.Expand(roadClearanceThreshold * 2); 
            cachedRoads.Add(new RoadData { container = c, worldBounds = b });
        }
    }

    private void SaveSettings() {
        if (buildingPrefab != null) EditorPrefs.SetString(KEY_PREFAB, AssetDatabase.GetAssetPath(buildingPrefab));
        EditorPrefs.SetString(KEY_PARENT, parentObjectName);
        EditorPrefs.SetInt(KEY_TERRAIN, terrainLayer);
        EditorPrefs.SetInt(KEY_OBSTACLE, obstacleMask);
        EditorPrefs.SetInt(KEY_TARGET, buildingTargetLayer);
        EditorPrefs.SetFloat(KEY_STEP, stepDistance);
        EditorPrefs.SetFloat(KEY_CURB, curbOffset);
        EditorPrefs.SetFloat(KEY_CLEARANCE, roadClearanceThreshold);
        EditorPrefs.SetFloat(KEY_RAYHEIGHT, raycastHeight);
        EditorPrefs.SetInt(KEY_LIMIT, previewLimit);
        EditorPrefs.SetBool(KEY_RANDSCALE, randomizeScale);
        EditorPrefs.SetBool(KEY_UNIFORMSCALE, uniformScale);
        EditorPrefs.SetFloat(KEY_SCALEMIN, scaleRange.x);
        EditorPrefs.SetFloat(KEY_SCALEMAX, scaleRange.y);
        EditorPrefs.SetFloat(KEY_SCALEXMIN, scaleRangeX.x);
        EditorPrefs.SetFloat(KEY_SCALEXMAX, scaleRangeX.y);
        EditorPrefs.SetFloat(KEY_SCALEYMIN, scaleRangeY.x);
        EditorPrefs.SetFloat(KEY_SCALEYMAX, scaleRangeY.y);
        EditorPrefs.SetFloat(KEY_SCALEZMIN, scaleRangeZ.x);
        EditorPrefs.SetFloat(KEY_SCALEZMAX, scaleRangeZ.y);
    }

    private void LoadSettings() {
        string prefabPath = EditorPrefs.GetString(KEY_PREFAB, "");
        if (!string.IsNullOrEmpty(prefabPath)) buildingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        parentObjectName = EditorPrefs.GetString(KEY_PARENT, "Buildings");
        terrainLayer = EditorPrefs.GetInt(KEY_TERRAIN, 0);
        obstacleMask = EditorPrefs.GetInt(KEY_OBSTACLE, 0);
        buildingTargetLayer = EditorPrefs.GetInt(KEY_TARGET, 0);
        stepDistance = EditorPrefs.GetFloat(KEY_STEP, 25f);
        curbOffset = EditorPrefs.GetFloat(KEY_CURB, 10f);
        roadClearanceThreshold = EditorPrefs.GetFloat(KEY_CLEARANCE, 7f);
        raycastHeight = EditorPrefs.GetFloat(KEY_RAYHEIGHT, 2000f);
        previewLimit = EditorPrefs.GetInt(KEY_LIMIT, 2000);
        randomizeScale = EditorPrefs.GetBool(KEY_RANDSCALE, true);
        uniformScale = EditorPrefs.GetBool(KEY_UNIFORMSCALE, true);
        scaleRange = new Vector2(EditorPrefs.GetFloat(KEY_SCALEMIN, 0.8f), EditorPrefs.GetFloat(KEY_SCALEMAX, 1.2f));
        scaleRangeX = new Vector2(EditorPrefs.GetFloat(KEY_SCALEXMIN, 0.8f), EditorPrefs.GetFloat(KEY_SCALEXMAX, 1.2f));
        scaleRangeY = new Vector2(EditorPrefs.GetFloat(KEY_SCALEYMIN, 0.8f), EditorPrefs.GetFloat(KEY_SCALEYMAX, 1.2f));
        scaleRangeZ = new Vector2(EditorPrefs.GetFloat(KEY_SCALEZMIN, 0.8f), EditorPrefs.GetFloat(KEY_SCALEZMAX, 1.2f));
    }

    private void CleanupBuildings() {
        var all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var go in all) if (go.name == GENERATED_NAME) Undo.DestroyObjectImmediate(go);
    }

    private Vector3 GetPrefabSize(GameObject prefab) {
        if (prefab == null) return new Vector3(5, 5, 5);
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Vector3(5, 5, 5);
        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) combinedBounds.Encapsulate(renderers[i].bounds);
        return combinedBounds.size;
    }
}