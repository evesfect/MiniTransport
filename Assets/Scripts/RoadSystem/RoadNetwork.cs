using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoadNetwork : MonoBehaviour
{
    [Header("Debug Settings")]
    [Tooltip("Check this to visualize errors in the Scene View.")]
    public bool showDebugGizmos = true;
    
    [Header("Network State")]
    [SerializeField] private List<RoadNode> faultyNodes = new List<RoadNode>();
    [SerializeField] private List<RoadSegment> faultySegments = new List<RoadSegment>();
    
    [Tooltip("Assign your Node prefab here to allow Auto-Fixing of dead ends.")]
    public GameObject nodePrefab; 

    [Header("Terrain Tools")]
    public LayerMask terrainLayer = ~0; 
    [Tooltip("Height above terrain to place roads (prevents flickering).")]
    public float terrainSnapOffset = 0.2f; // NEW: Configurable Offset

    // --- 1. SCANNING LOGIC ---

    [ContextMenu("Scan Network")]
    public void ScanNetwork()
    {
        faultyNodes.Clear();
        faultySegments.Clear();

        var allNodes = GetComponentsInChildren<RoadNode>();
        foreach (var node in allNodes)
        {
            if (node.OutgoingRoads.Contains(null)) 
            {
                faultyNodes.Add(node);
                continue;
            }
            foreach(var seg in node.OutgoingRoads)
            {
                if (seg.StartNode != node && seg.EndNode != node)
                {
                    if(!faultyNodes.Contains(node)) faultyNodes.Add(node);
                }
            }
        }

        var allSegments = GetComponentsInChildren<RoadSegment>();
        foreach (var seg in allSegments)
        {
            if (seg.StartNode == null || seg.EndNode == null)
            {
                faultySegments.Add(seg);
            }
        }

        if (faultyNodes.Count > 0 || faultySegments.Count > 0)
        {
            Debug.LogWarning($"Scan Found: {faultyNodes.Count} Nodes with bad links, {faultySegments.Count} Broken Segments.");
            showDebugGizmos = true;
        }
        else
        {
            Debug.Log("Network is Clean!");
        }
    }

    // --- 2. FIXING LOGIC ---

    [ContextMenu("Fix Errors")]
    public void FixErrors()
    {
        ScanNetwork();

        int nodesFixed = 0;
        int segmentsFixed = 0;

        foreach (var node in faultyNodes)
        {
            if (node == null) continue;
            node.OutgoingRoads.RemoveAll(x => x == null);
            for (int i = node.OutgoingRoads.Count - 1; i >= 0; i--)
            {
                var seg = node.OutgoingRoads[i];
                if (seg != null && seg.StartNode != node && seg.EndNode != node)
                {
                    node.OutgoingRoads.RemoveAt(i);
                }
            }
            nodesFixed++;
        }

        foreach (var seg in faultySegments)
        {
            if (seg == null) continue;

            if (seg.StartNode == null)
            {
                Vector3 worldPos = seg.transform.TransformPoint(seg.Spline[0].Position);
                seg.StartNode = GetOrCreateNodeAt(worldPos, "Terminal_Start");
            }

            if (seg.EndNode == null)
            {
                Vector3 worldPos = seg.transform.TransformPoint(seg.Spline[seg.Spline.Count - 1].Position);
                seg.EndNode = GetOrCreateNodeAt(worldPos, "Terminal_End");
            }
            segmentsFixed++;
        }

        faultyNodes.Clear();
        faultySegments.Clear();
        
        Debug.Log($"Fix Complete: Cleaned {nodesFixed} nodes, Repaired {segmentsFixed} segments.");
    }

    private RoadNode GetOrCreateNodeAt(Vector3 worldPos, string suffix)
    {
        Collider[] hits = Physics.OverlapSphere(worldPos, 0.5f);
        foreach(var hit in hits)
        {
            var node = hit.GetComponent<RoadNode>();
            if (node != null) return node;
        }

        if (nodePrefab != null)
        {
            Transform parent = transform.Find("Nodes");
            if (parent == null) parent = transform; 

            GameObject newObj = Instantiate(nodePrefab, worldPos, Quaternion.identity, parent);
            newObj.name = $"AutoNode_{suffix}_{Random.Range(1000,9999)}";
            return newObj.GetComponent<RoadNode>();
        }
        return null;
    }

    // --- 3. TERRAIN TOOLS ---

    [ContextMenu("Snap All to Terrain")]
    public void SnapToTerrain()
    {
        Undo.RecordObjects(GetComponentsInChildren<Transform>(), "Snap Network to Terrain");

        var nodes = GetComponentsInChildren<RoadNode>();
        foreach (var node in nodes) SnapObjectToGround(node.transform);

        var segments = GetComponentsInChildren<RoadSegment>();
        foreach (var seg in segments) SnapSplineToGround(seg);

        Debug.Log($"Snapped {nodes.Length} nodes and {segments.Length} roads to terrain (Offset: {terrainSnapOffset}).");
    }

    // Culls objects outside terrain
    public void CullOutsideTerrain()
    {
        Undo.RegisterCompleteObjectUndo(gameObject, "Cull Off-Map Roads");

        int deletedNodes = 0;
        int deletedSegments = 0;

        // 1. Cull Nodes
        var nodes = GetComponentsInChildren<RoadNode>();
        List<RoadNode> nodesToDelete = new List<RoadNode>();
        
        foreach (var node in nodes)
        {
            // Raycast check
            if (!CheckTerrainHit(node.transform.position))
            {
                nodesToDelete.Add(node);
            }
        }

        foreach(var node in nodesToDelete)
        {
            Undo.DestroyObjectImmediate(node.gameObject);
            deletedNodes++;
        }

        // 2. Cull Segments
        var segments = GetComponentsInChildren<RoadSegment>();
        List<RoadSegment> segmentsToDelete = new List<RoadSegment>();

        foreach (var seg in segments)
        {
            // Check middle of the road
            // If the road center is in the void, delete it.
            // (We could check endpoints too, but middle is a safe "average" check)
            if (seg.Spline.Count > 0)
            {
                // Get approx middle knot index
                int midIndex = seg.Spline.Count / 2;
                Vector3 worldPos = seg.transform.TransformPoint(seg.Spline[midIndex].Position);

                if (!CheckTerrainHit(worldPos))
                {
                    segmentsToDelete.Add(seg);
                }
            }
        }

        foreach (var seg in segmentsToDelete)
        {
            Undo.DestroyObjectImmediate(seg.gameObject);
            deletedSegments++;
        }

        Debug.Log($"Culled {deletedNodes} Nodes and {deletedSegments} Segments that were outside the terrain.");

        // Auto-Run Clean to remove broken links from surviving nodes
        FixErrors(); 
    }

    // Helper for Raycast Logic
    private bool CheckTerrainHit(Vector3 targetPos)
    {
        Vector3 rayOrigin = targetPos;
        rayOrigin.y = 2000f; // Start high
        return Physics.Raycast(rayOrigin, Vector3.down, 4000f, terrainLayer);
    }

    private void SnapObjectToGround(Transform t)
    {
        Vector3 rayOrigin = t.position;
        rayOrigin.y = 2000f; 

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 4000f, terrainLayer))
        {
            Vector3 newPos = t.position;
            newPos.y = hit.point.y + terrainSnapOffset; // Uses configurable offset
            t.position = newPos;
        }
    }

    private void SnapSplineToGround(RoadSegment seg)
    {
        if (seg == null) return;
        var container = seg.GetComponent<UnityEngine.Splines.SplineContainer>();
        if (container == null) return;

        // 1. Record Undo (Essential for saving!)
        Undo.RecordObject(container, "Snap Spline Segment");

        var spline = container.Spline;
        
        for (int i = 0; i < spline.Count; i++)
        {
            var knot = spline[i];
            bool isStart = (i == 0);
            bool isEnd = (i == spline.Count - 1);
            Vector3 worldPos;

            if (isStart && seg.StartNode != null)
            {
                worldPos = seg.StartNode.transform.position;
            }
            else if (isEnd && seg.EndNode != null)
            {
                worldPos = seg.EndNode.transform.position;
            }
            else
            {
                worldPos = container.transform.TransformPoint(knot.Position);
                Vector3 rayOrigin = worldPos;
                rayOrigin.y = 2000f; 

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 4000f, terrainLayer))
                {
                    worldPos.y = hit.point.y + terrainSnapOffset; 
                }
            }

            knot.Position = container.transform.InverseTransformPoint(worldPos);
            spline[i] = knot; // This updates the data in memory
        }
        
        // 2. Force Dirty (Ensures the asterisk * appears and changes are saved to disk)
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(container);
        }
    }

    [ContextMenu("Generate All Meshes")]
    public void GenerateAllMeshes()
    {
        // Find all SimpleRoadMesh scripts in children
        var meshGenerators = GetComponentsInChildren<SimpleRoadMesh>();
        
        foreach (var gen in meshGenerators)
        {
            gen.Generate();
        }
        
        Debug.Log($"Generated meshes for {meshGenerators.Length} road segments.");
    }

    // --- 4. VISUALIZATION ---

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        Gizmos.color = new Color(1, 0, 0, 0.5f);
        foreach (var node in faultyNodes)
        {
            if (node != null) Gizmos.DrawSphere(node.transform.position, 2f);
        }

        Gizmos.color = Color.yellow;
        foreach (var seg in faultySegments)
        {
            if (seg != null)
            {
                Gizmos.DrawWireCube(seg.transform.position, Vector3.one * 5f);
                
                Vector3 startPos = seg.transform.TransformPoint(seg.Spline[0].Position);
                Vector3 endPos = seg.transform.TransformPoint(seg.Spline[seg.Spline.Count-1].Position);

                if (seg.StartNode == null)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(startPos, 1.5f);
                }
                if (seg.EndNode == null)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(endPos, 1.5f);
                }
            }
        }
    }
}

// --- CUSTOM EDITOR GUI ---
#if UNITY_EDITOR
[CustomEditor(typeof(RoadNetwork))]
public class RoadNetworkEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        RoadNetwork script = (RoadNetwork)target;

        GUILayout.Space(20);
        GUILayout.Label("Network Tools", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1. SCAN", GUILayout.Height(30)))
        {
            script.ScanNetwork();
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("2. FIX (Auto-Repair)", GUILayout.Height(30)))
        {
            Undo.RecordObject(script, "Fix Road Network");
            script.FixErrors();
            SceneView.RepaintAll();
        }
        GUILayout.EndHorizontal();
        
        GUILayout.Space(10);
        GUI.backgroundColor = new Color(0.5f, 0.8f, 1f); 
        if (GUILayout.Button("3. SNAP TO TERRAIN", GUILayout.Height(40)))
        {
            script.SnapToTerrain();
        }
        GUI.backgroundColor = Color.white;

        // --- NEW BUTTON ---
        GUILayout.Space(5);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f); // Red
        if (GUILayout.Button("4. CULL OFF-MAP OBJECTS", GUILayout.Height(30)))
        {
            script.CullOutsideTerrain();
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;
        // ------------------

        GUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "Cull Button: Deletes any Nodes or Roads that do not Raycast hit the terrain.", 
            MessageType.Warning);

        GUILayout.Space(5);
        GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f); 
        if (GUILayout.Button("5. GENERATE MESHES", GUILayout.Height(30)))
        {
            script.GenerateAllMeshes();
        }
        GUI.backgroundColor = Color.white;
    }
}
#endif