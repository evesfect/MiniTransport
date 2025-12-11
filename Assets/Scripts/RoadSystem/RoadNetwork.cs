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
    [SerializeField] private List<RoadNode> _faultyNodes = new List<RoadNode>();
    [SerializeField] private List<RoadSegment> _faultySegments = new List<RoadSegment>();
    
    [Tooltip("Assign your Node prefab here to allow Auto-Fixing of dead ends.")]
    public GameObject nodePrefab; 

    [Header("Terrain Tools")]
    public LayerMask terrainLayer = ~0; 
    [Tooltip("Height above terrain to place roads (prevents flickering).")]
    public float terrainSnapOffset = 0.2f; 

    [Header("Mesh Generation")]
    [Tooltip("Width applied to all roads when clicking Generate.")]
    public float globalRoadWidth = 8.0f;
    
    [Tooltip("Distance from center line for lanes. Applied to all segments via 'Update Lane Offsets'.")]
    public float globalLaneOffset = 2.0f;

    [ContextMenu("Scan Network")]
    public void ScanNetwork()
    {
        _faultyNodes.Clear();
        _faultySegments.Clear();

        var allNodes = GetComponentsInChildren<RoadNode>();
        foreach (var node in allNodes)
        {
            // Null links in the list
            if (node.ConnectedRoads.Contains(null)) 
            {
                _faultyNodes.Add(node);
                continue;
            }

            // Validity of connections
            // Every road connected to this node MUST reference this node as either NodeA or NodeB
            foreach(var seg in node.ConnectedRoads)
            {
                if (seg.NodeA != node && seg.NodeB != node)
                {
                    if(!_faultyNodes.Contains(node)) _faultyNodes.Add(node);
                }
            }
        }

        var allSegments = GetComponentsInChildren<RoadSegment>();
        foreach (var seg in allSegments)
        {
            // Missing endpoints
            if (seg.NodeA == null || seg.NodeB == null)
            {
                _faultySegments.Add(seg);
            }
        }

        if (_faultyNodes.Count > 0 || _faultySegments.Count > 0)
        {
            Debug.LogWarning($"Scan Found: {_faultyNodes.Count} Nodes with bad links, {_faultySegments.Count} Broken Segments.");
            showDebugGizmos = true;
        }
        else
        {
            Debug.Log("Network is Clean!");
        }
    }

    [ContextMenu("Fix Errors")]
    public void FixErrors()
    {
        ScanNetwork();

        int nodesFixed = 0;
        int segmentsFixed = 0;

        // Node fix: remove nulls and segments that don't belong
        foreach (var node in _faultyNodes)
        {
            if (node == null) continue;
            
            // Remove nulls
            node.ConnectedRoads.RemoveAll(x => x == null);
            
            // Remove roads that don't point back to this node
            for (int i = node.ConnectedRoads.Count - 1; i >= 0; i--)
            {
                var seg = node.ConnectedRoads[i];
                if (seg != null && seg.NodeA != node && seg.NodeB != node)
                {
                    node.ConnectedRoads.RemoveAt(i);
                }
            }
            nodesFixed++;
        }

        // Segment fix: create missing nodes at endpoints
        foreach (var seg in _faultySegments)
        {
            if (seg == null) continue;

            // If NodeA is missing, check start of spline (index 0)
            if (seg.NodeA == null && seg.Spline.Count > 0)
            {
                Vector3 worldPos = seg.transform.TransformPoint(seg.Spline[0].Position);
                seg.NodeA = GetOrCreateNodeAt(worldPos, "Terminal_A");
                if (seg.NodeA != null && !seg.NodeA.ConnectedRoads.Contains(seg))
                    seg.NodeA.ConnectedRoads.Add(seg);
            }

            // If NodeB is missing, check end of spline
            if (seg.NodeB == null && seg.Spline.Count > 0)
            {
                Vector3 worldPos = seg.transform.TransformPoint(seg.Spline[seg.Spline.Count - 1].Position);
                seg.NodeB = GetOrCreateNodeAt(worldPos, "Terminal_B");
                if (seg.NodeB != null && !seg.NodeB.ConnectedRoads.Contains(seg))
                    seg.NodeB.ConnectedRoads.Add(seg);
            }
            segmentsFixed++;
        }

        _faultyNodes.Clear();
        _faultySegments.Clear();
        
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

    public void CullOutsideTerrain()
    {
        Undo.RegisterCompleteObjectUndo(gameObject, "Cull Off-Map Roads");

        int deletedNodes = 0;
        int deletedSegments = 0;

        // Cull Nodes
        var nodes = GetComponentsInChildren<RoadNode>();
        List<RoadNode> nodesToDelete = new List<RoadNode>();
        
        foreach (var node in nodes)
        {
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

        // Cull Segments
        var segments = GetComponentsInChildren<RoadSegment>();
        List<RoadSegment> segmentsToDelete = new List<RoadSegment>();

        foreach (var seg in segments)
        {
            if (seg.Spline.Count > 0)
            {
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

        FixErrors(); 
    }

    private bool CheckTerrainHit(Vector3 targetPos)
    {
        Vector3 rayOrigin = targetPos;
        rayOrigin.y = 2000f; 
        return Physics.Raycast(rayOrigin, Vector3.down, 4000f, terrainLayer);
    }

    private void SnapObjectToGround(Transform t)
    {
        Vector3 rayOrigin = t.position;
        rayOrigin.y = 2000f; 

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 4000f, terrainLayer))
        {
            Vector3 newPos = t.position;
            newPos.y = hit.point.y + terrainSnapOffset; 
            t.position = newPos;
        }
    }

    private void SnapSplineToGround(RoadSegment seg)
    {
        if (seg == null) return;
        var container = seg.GetComponent<UnityEngine.Splines.SplineContainer>();
        if (container == null) return;

        Undo.RecordObject(container, "Snap Spline Segment");

        var spline = container.Spline;
        
        for (int i = 0; i < spline.Count; i++)
        {
            var knot = spline[i];
            bool isNodeA = (i == 0);
            bool isNodeB = (i == spline.Count - 1);
            Vector3 worldPos;

            // Use the Nodes to dictate the endpoint height if they exist
            if (isNodeA && seg.NodeA != null)
            {
                worldPos = seg.NodeA.transform.position;
            }
            else if (isNodeB && seg.NodeB != null)
            {
                worldPos = seg.NodeB.transform.position;
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
            spline[i] = knot; 
        }
        
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(container);
        }
    }

    [ContextMenu("Generate All Meshes")]
    public void GenerateAllMeshes()
    {
        var meshGenerators = GetComponentsInChildren<SimpleRoadMesh>();
        foreach (var gen in meshGenerators)
        {
            gen.roadWidth = globalRoadWidth;
            gen.Generate();
        }
        Debug.Log($"Generated meshes for {meshGenerators.Length} road segments.");
    }

    [ContextMenu("Clear All Meshes")]
    public void ClearAllMeshes()
    {
        var meshGenerators = GetComponentsInChildren<SimpleRoadMesh>();
        foreach (var gen in meshGenerators)
        {
            // Destroy the mesh object to clear memory
            var filter = gen.GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = null;

            var col = gen.GetComponent<MeshCollider>();
            if (col != null) col.sharedMesh = null;
        }
        Debug.Log($"Cleared meshes for {meshGenerators.Length} road segments.");
    }

    [ContextMenu("Update Lane Offsets")]
    public void UpdateLaneOffsets()
    {
        var segments = GetComponentsInChildren<RoadSegment>();
        foreach (var seg in segments)
        {
            seg.laneOffset = globalLaneOffset;
#if UNITY_EDITOR
            EditorUtility.SetDirty(seg); // Mark as changed so Unity saves it
#endif
        }
        Debug.Log($"Updated lane offset to {globalLaneOffset} for {segments.Length} road segments.");
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        Gizmos.color = new Color(1, 0, 0, 0.5f);
        foreach (var node in _faultyNodes)
        {
            if (node != null) Gizmos.DrawSphere(node.transform.position, 2f);
        }

        Gizmos.color = Color.yellow;
        foreach (var seg in _faultySegments)
        {
            if (seg != null)
            {
                Gizmos.DrawWireCube(seg.transform.position, Vector3.one * 5f);
                
                if (seg.Spline.Count > 0)
                {
                    Vector3 startPos = seg.transform.TransformPoint(seg.Spline[0].Position);
                    Vector3 endPos = seg.transform.TransformPoint(seg.Spline[seg.Spline.Count-1].Position);

                    if (seg.NodeA == null)
                    {
                        Gizmos.color = Color.red;
                        Gizmos.DrawSphere(startPos, 1.5f);
                    }
                    if (seg.NodeB == null)
                    {
                        Gizmos.color = Color.red;
                        Gizmos.DrawSphere(endPos, 1.5f);
                    }
                }
            }
        }
    }
}

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

        GUILayout.Space(5);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f); 
        if (GUILayout.Button("4. CULL OFF-MAP OBJECTS", GUILayout.Height(30)))
        {
            script.CullOutsideTerrain();
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "Cull Button: Deletes any Nodes or Roads that do not Raycast hit the terrain.", 
            MessageType.Warning);

        GUILayout.Space(10);
        EditorGUILayout.HelpBox("Mesh Tools: Use 'Global Road Width' above to set width for all roads.", MessageType.Info);
        
        GUILayout.Space(5);
        if (GUILayout.Button("UPDATE LANE OFFSETS", GUILayout.Height(30)))
        {
            script.UpdateLaneOffsets();
        }

        GUILayout.Space(5);
        GUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f); 
        if (GUILayout.Button("5. GENERATE MESHES", GUILayout.Height(30)))
        {
            script.GenerateAllMeshes();
        }
        
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f); // Red tint for destructive action
        if (GUILayout.Button("CLEAR MESHES", GUILayout.Height(30)))
        {
            script.ClearAllMeshes();
        }
        
        GUILayout.EndHorizontal();
        GUI.backgroundColor = Color.white;
    }
}
#endif