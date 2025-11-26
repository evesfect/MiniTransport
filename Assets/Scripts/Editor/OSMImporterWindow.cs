using UnityEngine;
using UnityEditor;
using UnityEngine.Splines;
using OsmSharp;
using OsmSharp.Streams;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class OSMImporterWindow : EditorWindow
{
    // --- SETTINGS ---
    string osmFilePath = "";
    float mapScale = 1.0f;
    bool snapToTerrain = false;
    LayerMask terrainLayer = ~0; 
    
    // --- REFERENCES ---
    GameObject nodePrefab;     
    GameObject segmentPrefab;  

    // --- INTERNAL DATA ---
    Dictionary<long, Vector3> relevantNodePositions = new Dictionary<long, Vector3>();
    List<Way> highwayWays = new List<Way>();
    HashSet<long> intersectionIDs = new HashSet<long>();
    double originLat = 0;
    double originLon = 0;
    bool hasOrigin = false;
    Dictionary<long, RoadNode> spawnedNodeMap = new Dictionary<long, RoadNode>();

    // --- PREFS KEYS ---
    private const string PREF_PATH = "OSM_FilePath";
    private const string PREF_SCALE = "OSM_Scale";
    private const string PREF_SNAP = "OSM_Snap";
    private const string PREF_LAYER = "OSM_Layer";

    [MenuItem("Tools/OSM Road Importer")]
    public static void ShowWindow()
    {
        GetWindow<OSMImporterWindow>("OSM Importer");
    }

    // 1. LOAD SETTINGS ON OPEN
    private void OnEnable()
    {
        osmFilePath = EditorPrefs.GetString(PREF_PATH, "Assets/map.osm");
        mapScale = EditorPrefs.GetFloat(PREF_SCALE, 1.0f);
        snapToTerrain = EditorPrefs.GetBool(PREF_SNAP, false);
        terrainLayer = EditorPrefs.GetInt(PREF_LAYER, ~0);
    }

    // 2. SAVE SETTINGS ON CLOSE/CHANGE
    private void OnDisable()
    {
        SaveSettings();
    }

    void SaveSettings()
    {
        EditorPrefs.SetString(PREF_PATH, osmFilePath);
        EditorPrefs.SetFloat(PREF_SCALE, mapScale);
        EditorPrefs.SetBool(PREF_SNAP, snapToTerrain);
        EditorPrefs.SetInt(PREF_LAYER, terrainLayer);
    }

    void OnGUI()
    {
        GUILayout.Label("OSM Import Settings", EditorStyles.boldLabel);

        // File Selection
        GUILayout.BeginHorizontal();
        string newPath = EditorGUILayout.TextField("File Path", osmFilePath);
        if (newPath != osmFilePath) { osmFilePath = newPath; SaveSettings(); }
        
        if (GUILayout.Button("Browse", GUILayout.Width(75)))
        {
            string selected = EditorUtility.OpenFilePanel("Select OSM/PBF File", Application.dataPath, "pbf,osm");
            if (!string.IsNullOrEmpty(selected)) 
            {
                // Make relative to project if possible
                if (selected.StartsWith(Application.dataPath)) 
                    osmFilePath = "Assets" + selected.Substring(Application.dataPath.Length);
                else 
                    osmFilePath = selected;
                    
                SaveSettings();
            }
        }
        GUILayout.EndHorizontal();

        // Prefabs (EditorPrefs doesn't handle Object references well, usually best to just drag them or find by name)
        // Optimization: Try to auto-load if null
        if (nodePrefab == null) nodePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RoadNode.prefab"); // Change path to match yours
        
        nodePrefab = (GameObject)EditorGUILayout.ObjectField("Intersection Prefab", nodePrefab, typeof(GameObject), false);
        segmentPrefab = (GameObject)EditorGUILayout.ObjectField("Road Segment Prefab", segmentPrefab, typeof(GameObject), false);

        // Parameters
        float newScale = EditorGUILayout.FloatField("Scale Factor", mapScale);
        if (newScale != mapScale) { mapScale = newScale; SaveSettings(); }

        bool newSnap = EditorGUILayout.Toggle("Snap to Terrain", snapToTerrain);
        if (newSnap != snapToTerrain) { snapToTerrain = newSnap; SaveSettings(); }

        if(snapToTerrain)
        {
            int newLayer = EditorGUILayout.LayerField("Terrain Layer", terrainLayer);
            if (newLayer != terrainLayer) { terrainLayer = newLayer; SaveSettings(); }
        }

        GUILayout.Space(10);

        if (GUILayout.Button("GENERATE MAP", GUILayout.Height(40)))
        {
            if (CheckReferences()) RunImportProcess();
        }
    }

    bool CheckReferences()
    {
        if (!File.Exists(osmFilePath)) { EditorUtility.DisplayDialog("Error", "File not found!", "OK"); return false; }
        if (nodePrefab == null || segmentPrefab == null) { EditorUtility.DisplayDialog("Error", "Assign Prefabs first!", "OK"); return false; }
        return true;
    }

    void RunImportProcess()
    {
        ClearInternalData();
        ReadWaysPass();
        ReadNodesPass();
        SpawnNetwork();
        Debug.Log("OSM Import Complete.");
    }

    // ---------------------------------------------------------
    // LOGIC: READING DATA
    // ---------------------------------------------------------

    void ReadWaysPass()
    {
        Dictionary<long, int> nodeUsageCount = new Dictionary<long, int>();

        using (var fileStream = File.OpenRead(osmFilePath))
        {
            var source = GetStreamSource(fileStream);
            
            // Filter: Must be Way, Must have 'highway', Must NOT be 'footway' or 'pedestrian' if you strictly want buses
            var highways = source.Where(x => 
                x.Type == OsmGeoType.Way && 
                x.Tags != null && 
                x.Tags.ContainsKey("highway") &&
                !x.Tags.GetValue("highway").Contains("footway") // Optional filter
            );

            foreach (var element in highways)
            {
                Way w = (Way)element;
                if (w.Nodes == null || w.Nodes.Length < 2) continue;

                highwayWays.Add(w);

                // Count node usage
                foreach (long nodeId in w.Nodes)
                {
                    if (!nodeUsageCount.ContainsKey(nodeId)) nodeUsageCount[nodeId] = 0;
                    nodeUsageCount[nodeId]++;
                }
            }
        }

        // Determine Intersections & Terminals
        foreach (var kvp in nodeUsageCount)
        {
            // Condition 1: It is a junction (used by 2+ ways or shared by segments)
            bool isJunction = kvp.Value > 1;

            // Condition 2: It is a Layout Terminal (Start or End of a Way)
            // We need to check if this NodeID appears at the start/end of ANY highway way
            bool isTerminal = false;
            
            // Optimization: We can check this during the loop above, 
            // but for clarity, we check if this ID is a start/end of our cached ways.
            // (Since this is Editor code, a little O(N) is fine for safety)
            if (!isJunction) // Only check if not already a junction
            {
                foreach(var way in highwayWays)
                {
                    if (way.Nodes[0] == kvp.Key || way.Nodes[way.Nodes.Length-1] == kvp.Key)
                    {
                        isTerminal = true;
                        break;
                    }
                }
            }

            // RESULT: We treat it as a Node if it's a Junction OR a Terminal
            if (isJunction || isTerminal)
            {
                intersectionIDs.Add(kvp.Key);
            }
        }
        
        Debug.Log($"Pass 1: Found {highwayWays.Count} roads and {intersectionIDs.Count} intersections.");
    }

    void ReadNodesPass()
    {
        // We need to know WHICH nodes to keep so we don't store 100k building nodes
        HashSet<long> neededNodes = new HashSet<long>();
        foreach(var way in highwayWays)
        {
            foreach(var id in way.Nodes) neededNodes.Add(id);
        }

        using (var fileStream = File.OpenRead(osmFilePath))
        {
            var source = GetStreamSource(fileStream);
            
            // Iterate ALL objects, pick out the Nodes
            foreach (var element in source)
            {
                if (element.Type == OsmGeoType.Node)
                {
                    if (neededNodes.Contains(element.Id.Value))
                    {
                        OsmSharp.Node n = (OsmSharp.Node)element;
                        if (!n.Latitude.HasValue || !n.Longitude.HasValue) continue;

                        // Set Origin if first node
                        if (!hasOrigin)
                        {
                            originLat = n.Latitude.Value;
                            originLon = n.Longitude.Value;
                            hasOrigin = true;
                        }

                        Vector3 worldPos = GeoToWorld(n.Latitude.Value, n.Longitude.Value);
                        relevantNodePositions.Add(element.Id.Value, worldPos);
                    }
                }
            }
        }
        Debug.Log($"Pass 2: Cached {relevantNodePositions.Count} node positions.");
    }

    // Helper to switch between XML and PBF based on extension
    OsmStreamSource GetStreamSource(FileStream stream)
    {
        if (osmFilePath.EndsWith(".pbf"))
        {
            return new PBFOsmStreamSource(stream);
        }
        else
        {
            return new XmlOsmStreamSource(stream);
        }
    }

    // ---------------------------------------------------------
    // LOGIC: GENERATION
    // ---------------------------------------------------------

    void SpawnNetwork()
    {
        GameObject root = new GameObject("OSM_Generated_Map");
        
        // --- FEATURE 1: AUTO-ASSIGN PREFAB ---
        RoadNetwork networkScript = root.AddComponent<RoadNetwork>();
        networkScript.nodePrefab = this.nodePrefab; // Pass the reference!
        networkScript.showDebugGizmos = false; // Start clean
        // -------------------------------------

        GameObject intersectionsGroup = new GameObject("Nodes");
        GameObject segmentsGroup = new GameObject("Roads");
        intersectionsGroup.transform.parent = root.transform;
        segmentsGroup.transform.parent = root.transform;

        // 1. SPAWN INTERSECTION OBJECTS
        foreach (long id in intersectionIDs)
        {
            if (!relevantNodePositions.ContainsKey(id)) continue;

            Vector3 pos = relevantNodePositions[id];
            
            GameObject nodeObj = (GameObject)PrefabUtility.InstantiatePrefab(nodePrefab, intersectionsGroup.transform);
            nodeObj.transform.position = pos;
            nodeObj.name = $"Node_{id}";

            RoadNode nodeScript = nodeObj.GetComponent<RoadNode>();
            nodeScript.OSM_NodeID = id;
            
            spawnedNodeMap.Add(id, nodeScript);
        }

        // 2. SPAWN SEGMENTS
        int segmentCount = 0;
        foreach (Way way in highwayWays)
        {
            List<long> segmentNodeIDs = new List<long>();
            for (int i = 0; i < way.Nodes.Length; i++)
            {
                long currentNodeID = way.Nodes[i];
                segmentNodeIDs.Add(currentNodeID);

                bool isIntersection = intersectionIDs.Contains(currentNodeID);
                bool isLastInWay = (i == way.Nodes.Length - 1);
                bool hasGeometry = segmentNodeIDs.Count > 1;

                if (hasGeometry && (isIntersection || isLastInWay))
                {
                    CreateSegmentObject(segmentNodeIDs, segmentsGroup.transform, way);
                    segmentCount++;
                    
                    long overlapNodeID = segmentNodeIDs[segmentNodeIDs.Count - 1];
                    segmentNodeIDs.Clear();
                    segmentNodeIDs.Add(overlapNodeID);
                }
            }
        }
    }

    void CreateSegmentObject(List<long> ids, Transform parent, Way originalWay)
    {
        GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(segmentPrefab, parent);
        obj.name = $"Segment_{ids[0]}_{ids[ids.Count-1]}";

        // FIX: Force Origin for World-Space Knots
        obj.transform.position = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;

        SplineContainer splineContainer = obj.GetComponent<SplineContainer>();
        Spline spline = splineContainer.Spline;
        spline.Clear();

        foreach (long id in ids)
        {
            if (relevantNodePositions.TryGetValue(id, out Vector3 pos))
            {
                spline.Add(new BezierKnot(pos));
            }
        }
        spline.SetTangentMode(TangentMode.AutoSmooth);

        RoadSegment segmentScript = obj.GetComponent<RoadSegment>();
        
        if (spawnedNodeMap.TryGetValue(ids[0], out RoadNode startNode))
        {
            segmentScript.StartNode = startNode;
            startNode.OutgoingRoads.Add(segmentScript);
        }

        if (spawnedNodeMap.TryGetValue(ids[ids.Count - 1], out RoadNode endNode))
        {
            segmentScript.EndNode = endNode;
        }

        segmentScript.CalculateLength();
    }

    // ---------------------------------------------------------
    // LOGIC: MATH & UTILS
    // ---------------------------------------------------------

    Vector3 GeoToWorld(double lat, double lon)
    {
        double R = 6378137; // Earth Radius meters
        double dLat = (lat - originLat) * Mathf.Deg2Rad;
        double dLon = (lon - originLon) * Mathf.Deg2Rad;
        
        // Simple projection
        float x = (float)(dLon * System.Math.Cos(originLat * Mathf.Deg2Rad) * R);
        float z = (float)(dLat * R);

        Vector3 pos = new Vector3(x * mapScale, 0, z * mapScale);

        if (snapToTerrain)
        {
            RaycastHit hit;
            // Raycast from high up
            if (Physics.Raycast(pos + Vector3.up * 2000f, Vector3.down, out hit, 4000f, terrainLayer))
            {
                pos.y = hit.point.y;
            }
        }

        return pos;
    }

    void ClearInternalData()
    {
        relevantNodePositions.Clear();
        highwayWays.Clear();
        intersectionIDs.Clear();
        spawnedNodeMap.Clear();
        hasOrigin = false;
    }
}