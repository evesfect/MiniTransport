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
    
    // Reference to the Node Prefab so we can auto-spawn missing terminals
    [Tooltip("Assign your Node prefab here to allow Auto-Fixing of dead ends.")]
    public GameObject nodePrefab; 

    [Header("Terrain Tools")]
    public LayerMask terrainLayer = ~0; // Default to everything

    // --- 1. SCANNING LOGIC ---

    [ContextMenu("Scan Network")]
    public void ScanNetwork()
    {
        faultyNodes.Clear();
        faultySegments.Clear();

        // A. Check Nodes
        var allNodes = GetComponentsInChildren<RoadNode>();
        foreach (var node in allNodes)
        {
            // Error 1: The list contains 'null' (deleted objects)
            if (node.OutgoingRoads.Contains(null)) 
            {
                faultyNodes.Add(node);
                continue;
            }

            // Error 2: Ghost Links (Node says "I go to Road A", but Road A says "I start at Node B")
            foreach(var seg in node.OutgoingRoads)
            {
                if (seg.StartNode != node && seg.EndNode != node)
                {
                    if(!faultyNodes.Contains(node)) faultyNodes.Add(node);
                }
            }
        }

        // B. Check Segments
        var allSegments = GetComponentsInChildren<RoadSegment>();
        foreach (var seg in allSegments)
        {
            // Error: Missing an endpoint. 
            // This is "Faulty" because pathfinding needs a node to stop at.
            if (seg.StartNode == null || seg.EndNode == null)
            {
                faultySegments.Add(seg);
            }
        }

        // Reporting
        if (faultyNodes.Count > 0 || faultySegments.Count > 0)
        {
            Debug.LogWarning($"Scan Found: {faultyNodes.Count} Nodes with bad links, {faultySegments.Count} Broken Segments.");
            showDebugGizmos = true; // Auto-turn on gizmos
        }
        else
        {
            Debug.Log("Network is Clean! (No topology errors found)");
        }
    }

    // --- 2. FIXING LOGIC ---

    [ContextMenu("Fix Errors")]
    public void FixErrors()
    {
        ScanNetwork(); // Always scan first to get fresh lists

        int nodesFixed = 0;
        int segmentsFixed = 0;

        // A. Fix Nodes (Cleanup)
        foreach (var node in faultyNodes)
        {
            if (node == null) continue;
            
            // 1. Remove Nulls
            node.OutgoingRoads.RemoveAll(x => x == null);

            // 2. Remove Ghost Links
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

        // B. Fix Segments (Repair or Create Terminals)
        foreach (var seg in faultySegments)
        {
            if (seg == null) continue;

            // Fix Start
            if (seg.StartNode == null)
            {
                Vector3 pos = seg.Spline[0].Position; 
                // Convert local spline point to world
                Vector3 worldPos = seg.transform.TransformPoint(pos);
                
                seg.StartNode = GetOrCreateNodeAt(worldPos, "Terminal_Start");
            }

            // Fix End
            if (seg.EndNode == null)
            {
                // Get last knot
                Vector3 pos = seg.Spline[seg.Spline.Count - 1].Position;
                Vector3 worldPos = seg.transform.TransformPoint(pos);
                
                seg.EndNode = GetOrCreateNodeAt(worldPos, "Terminal_End");
            }

            segmentsFixed++;
        }

        // Clear errors
        faultyNodes.Clear();
        faultySegments.Clear();
        
        Debug.Log($"Fix Complete: Cleaned {nodesFixed} nodes, Repaired {segmentsFixed} segments (created missing terminals).");
    }

    // Helper: Tries to find a node, or spawns one if missing
    private RoadNode GetOrCreateNodeAt(Vector3 worldPos, string suffix)
    {
        // 1. Try to find existing node (Sphere check)
        Collider[] hits = Physics.OverlapSphere(worldPos, 0.5f);
        foreach(var hit in hits)
        {
            var node = hit.GetComponent<RoadNode>();
            if (node != null) return node;
        }

        // 2. Create New (if we have a prefab)
        if (nodePrefab != null)
        {
            // Find the "Nodes" folder to keep hierarchy clean
            Transform parent = transform.Find("Nodes");
            if (parent == null) parent = transform; // Fallback to root

            GameObject newObj = Instantiate(nodePrefab, worldPos, Quaternion.identity, parent);
            newObj.name = $"AutoNode_{suffix}_{Random.Range(1000,9999)}";
            return newObj.GetComponent<RoadNode>();
        }
        else
        {
            Debug.LogError("Cannot auto-fix missing node: No Node Prefab assigned in RoadNetwork script!");
            return null;
        }
    }

    // --- 3. VISUALIZATION ---

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        // Draw Faulty Nodes (Red Spheres)
        Gizmos.color = new Color(1, 0, 0, 0.5f);
        foreach (var node in faultyNodes)
        {
            if (node != null)
            {
                Gizmos.DrawSphere(node.transform.position, 2f);
                // Draw line to the bad segment reference? 
                // Hard to visualize null, but we mark the node itself.
            }
        }

        // Draw Faulty Segments (Yellow Dashed Lines)
        Gizmos.color = Color.yellow;
        foreach (var seg in faultySegments)
        {
            if (seg != null)
            {
                // Highlight the road center
                Gizmos.DrawWireCube(seg.transform.position, Vector3.one * 5f);
                
                // Draw specific markers for missing ends
                Vector3 startPos = seg.transform.TransformPoint(seg.Spline[0].Position);
                Vector3 endPos = seg.transform.TransformPoint(seg.Spline[seg.Spline.Count-1].Position);

                if (seg.StartNode == null)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(startPos, 1.5f);
                    Gizmos.DrawIcon(startPos, "console.erroricon.sml", true);
                }
                
                if (seg.EndNode == null)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(endPos, 1.5f);
                    Gizmos.DrawIcon(endPos, "console.erroricon.sml", true);
                }
            }
        }
    }

    [ContextMenu("Snap All to Terrain")]
    public void SnapToTerrain()
    {
        Undo.RecordObjects(GetComponentsInChildren<Transform>(), "Snap Network to Terrain");

        // 1. SNAP NODES (Intersections)
        var nodes = GetComponentsInChildren<RoadNode>();
        foreach (var node in nodes)
        {
            SnapObjectToGround(node.transform);
        }

        // 2. SNAP SPLINES (Road Geometry)
        var segments = GetComponentsInChildren<RoadSegment>();
        foreach (var seg in segments)
        {
            SnapSplineToGround(seg);
        }

        Debug.Log($"Snapped {nodes.Length} nodes and {segments.Length} roads to terrain.");
    }

    private void SnapObjectToGround(Transform t)
    {
        // 1. Setup Ray Origin (High up)
        Vector3 rayOrigin = t.position;
        rayOrigin.y = 2000f; 

        // 2. Raycast
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 4000f, terrainLayer))
        {
            // Hit Terrain: Snap to it
            Vector3 newPos = t.position;
            newPos.y = hit.point.y + 0.2f;
            t.position = newPos;
        }
        // Else: Do nothing (Keep original height)
    }

    private void SnapSplineToGround(RoadSegment seg)
    {
        if (seg == null) return;
        
        var container = seg.GetComponent<UnityEngine.Splines.SplineContainer>();
        if (container == null) return;

        var spline = container.Spline;
        
        for (int i = 0; i < spline.Count; i++)
        {
            var knot = spline[i]; 
            
            bool isStart = (i == 0);
            bool isEnd = (i == spline.Count - 1);

            Vector3 worldPos;

            // 1. Connection Points (Snap to Node)
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
                // 2. Middle Points (Snap to Terrain)
                worldPos = container.transform.TransformPoint(knot.Position);
                
                // FIX: Use a separate variable for the raycast origin
                Vector3 rayOrigin = worldPos;
                rayOrigin.y = 2000f; 

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 4000f, terrainLayer))
                {
                    worldPos.y = hit.point.y + 0.2f; 
                }
                // Else: worldPos.y remains whatever it was (flat), it won't jump to 2000.
            }

            // Apply back
            knot.Position = container.transform.InverseTransformPoint(worldPos);
            spline[i] = knot; 
        }
    }
}

// --- CUSTOM EDITOR GUI ---
#if UNITY_EDITOR
[CustomEditor(typeof(RoadNetwork))]
public class RoadNetworkEditor : Editor
{
    // Inside RoadNetworkEditor : Editor
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
        
        // --- NEW BUTTON ---
        GUILayout.Space(10);
        GUI.backgroundColor = new Color(0.5f, 0.8f, 1f); // Light Blue
        if (GUILayout.Button("3. SNAP TO TERRAIN", GUILayout.Height(40)))
        {
            script.SnapToTerrain();
        }
        GUI.backgroundColor = Color.white;
        // ------------------

        GUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "Snap Button: Raycasts all Nodes and Spline Knots down to the Terrain Layer defined above.", 
            MessageType.Info);
    }

}
#endif