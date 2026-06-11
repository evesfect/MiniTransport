using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class BuildingBrushTool : EditorWindow
{
    public GameObject targetObject;
    public LayerMask layerMask;
    public float brushSize = 10f;
    private bool isBrushEnabled = false;

    [MenuItem("Tools/Building Brush")]
    public static void ShowWindow() => GetWindow<BuildingBrushTool>("Building Brush");

    private void OnGUI()
    {
        GUILayout.Label("Brush Settings", EditorStyles.boldLabel);
        
        targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);
        layerMask = LayerField("Target Layer", layerMask);
        brushSize = EditorGUILayout.Slider("Brush Size", brushSize, 1f, 100f);
        
        GUI.backgroundColor = isBrushEnabled ? Color.green : Color.white;
        if (GUILayout.Toggle(isBrushEnabled, "Enable Brush", "Button", GUILayout.Height(30)) != isBrushEnabled)
        {
            isBrushEnabled = !isBrushEnabled;
            // Force subscription toggle to be clean
            SceneView.duringSceneGui -= OnSceneGUI;
            if (isBrushEnabled) SceneView.duringSceneGui += OnSceneGUI;
        }
        GUI.backgroundColor = Color.white;
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!isBrushEnabled) return;

        // 1. Trap the mouse so we don't select other things
        int controlID = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlID);

        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        
        // 2. Hit the specific layer
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, layerMask))
        {
            // Verify it's our specific target if assigned
            if (targetObject != null && hit.collider.gameObject != targetObject) return;

            DrawProjectedTexture(hit.point, brushSize, hit.collider as TerrainCollider);

            // 3. Precise Click Detection
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                ExecuteBrushAction(hit.point);
                e.Use(); // Consume event so Unity doesn't deselect the tool
            }
        }
        
        // Keep the preview smooth
        sceneView.Repaint();
    }

    private void DrawProjectedTexture(Vector3 center, float radius, TerrainCollider tc)
    {
        int segments = 32;
        Vector3[] vertices = new Vector3[segments];

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            Vector3 pos = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            
            // If it's a terrain, sample height. If a mesh, just use the hit.y
            if (tc != null)
                pos.y = tc.terrainData.GetInterpolatedHeight((pos.x - tc.transform.position.x) / tc.terrainData.size.x, (pos.z - tc.transform.position.z) / tc.terrainData.size.z) + tc.transform.position.y;
            else
                pos.y = center.y; // Simplified for flat meshes

            vertices[i] = pos + Vector3.up * 0.1f;
        }

        Handles.color = new Color(0, 0.6f, 1f, 0.3f);
        Handles.DrawAAConvexPolygon(vertices);
        Handles.color = new Color(0, 0.6f, 1f, 1f);
        Handles.DrawPolyLine(vertices);
        Handles.DrawLine(vertices[segments - 1], vertices[0]);
    }

    private void ExecuteBrushAction(Vector3 center)
    {
        Debug.Log("Brush clicked at: " + center);
    }

    // Helper to draw a LayerMask dropdown in the Editor
    private LayerMask LayerField(string label, LayerMask layerMask)
    {
        var layers = new List<string>();
        var layerNumbers = new List<int>();

        for (int i = 0; i < 32; i++)
        {
            string layerName = LayerMask.LayerToName(i);
            if (!string.IsNullOrEmpty(layerName))
            {
                layers.Add(layerName);
                layerNumbers.Add(i);
            }
        }
        int maskWithoutEmpty = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            if (((1 << layerNumbers[i]) & layerMask.value) > 0) maskWithoutEmpty |= (1 << i);
        }
        maskWithoutEmpty = EditorGUILayout.MaskField(label, maskWithoutEmpty, layers.ToArray());
        int finalMask = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            if ((maskWithoutEmpty & (1 << i)) > 0) finalMask |= (1 << layerNumbers[i]);
        }
        return finalMask;
    }

    private void OnDestroy() => SceneView.duringSceneGui -= OnSceneGUI;
}