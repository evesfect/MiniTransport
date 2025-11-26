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
    // We cache only the nodes that are actually part of highways to save memory
    Dictionary<long, Vector3> relevantNodePositions = new Dictionary<long, Vector3>();
    
    // We cache the ways we want to build
    List<Way> highwayWays = new List<Way>();
    
    // We store which nodes are intersections (appear in >1 way)
    HashSet<long> intersectionIDs = new HashSet<long>();

    // Map Origin (Lat/Lon)
    double originLat = 0;
    double originLon = 0;
    bool hasOrigin = false;

    // Track spawned nodes to link them later
    Dictionary<long, RoadNode> spawnedNodeMap = new Dictionary<long, RoadNode>();

    [MenuItem("Tools/OSM Road Importer")]
    public static void ShowWindow()
    {
        GetWindow<OSMImporterWindow>("OSM Importer");
    }

    void OnGUI()
    {
        GUILayout.Label("OSM Import Settings", EditorStyles.boldLabel);

        // File Selection
        GUILayout.BeginHorizontal();
        osmFilePath = EditorGUILayout.TextField("File Path", osmFilePath);
        if (GUILayout.Button("Browse", GUILayout.Width(75)))
        {
            osmFilePath = EditorUtility.OpenFilePanel("Select OSM/PBF File", Application.dataPath, "pbf,osm");
        }
        GUILayout.EndHorizontal();

        // Prefabs
        nodePrefab = (GameObject)EditorGUILayout.ObjectField("Intersection Prefab", nodePrefab, typeof(GameObject), false);
        segmentPrefab = (GameObject)EditorGUILayout.ObjectField("Road Segment Prefab", segmentPrefab, typeof(GameObject), false);

        // Parameters
        mapScale = EditorGUILayout.FloatField("Scale Factor", mapScale);
        snapToTerrain = EditorGUILayout.Toggle("Snap to Terrain", snapToTerrain);
        if(snapToTerrain)
            terrainLayer = EditorGUILayout.LayerField("Terrain Layer", terrainLayer);

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
        
        // PASS 1: READ WAYS
        // We scan strictly for 'highway' tags. We collect all Node IDs used by them.
        // We also identify which nodes are Intersections.
        ReadWaysPass();

        // PASS 2: READ NODES
        // We scan the file again (or just the nodes part).
        // We only store coordinates for nodes we found in Pass 1.
        ReadNodesPass();

        // PASS 3: SPAWN UNITY OBJECTS
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

        // Determine Intersections
        // A node is an intersection if it is used more than once OR if it is the end of a way (dead end / connector)
        // Note: For graph splitting, strictly >1 usage is the intersection. 
        // Ends of ways are naturally handled by the loop logic later.
        foreach (var kvp in nodeUsageCount)
        {
            if (kvp.Value > 1)
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
        GameObject intersectionsGroup = new GameObject("Nodes");
        GameObject segmentsGroup = new GameObject("Roads");
        intersectionsGroup.transform.parent = root.transform;
        segmentsGroup.transform.parent = root.transform;

        // 1. SPAWN INTERSECTION OBJECTS
        foreach (long id in intersectionIDs)
        {
            if (!relevantNodePositions.ContainsKey(id)) continue;

            Vector3 pos = relevantNodePositions[id];
            
            // Create Node
            GameObject nodeObj = (GameObject)PrefabUtility.InstantiatePrefab(nodePrefab, intersectionsGroup.transform);
            nodeObj.transform.position = pos;
            nodeObj.name = $"Node_{id}";

            RoadNode nodeScript = nodeObj.GetComponent<RoadNode>();
            nodeScript.OSM_NodeID = id;
            
            spawnedNodeMap.Add(id, nodeScript);
        }

        // 2. SPAWN & SPLIT SEGMENTS
        int segmentCount = 0;

        foreach (Way way in highwayWays)
        {
            // Accumulator for the current split
            List<long> segmentNodeIDs = new List<long>();

            for (int i = 0; i < way.Nodes.Length; i++)
            {
                long currentNodeID = way.Nodes[i];
                segmentNodeIDs.Add(currentNodeID);

                // Should we split here?
                // Yes if:
                // 1. We have at least 2 nodes in buffer (valid line)
                // 2. AND (Current Node is an intersection OR It is the very last node of the Way)
                // 3. AND (It is NOT the first node of the buffer - avoids single-point segments)
                
                bool isIntersection = intersectionIDs.Contains(currentNodeID);
                bool isLastInWay = (i == way.Nodes.Length - 1);
                bool hasGeometry = segmentNodeIDs.Count > 1;

                if (hasGeometry && (isIntersection || isLastInWay))
                {
                    // BUILD SEGMENT
                    CreateSegmentObject(segmentNodeIDs, segmentsGroup.transform, way);
                    segmentCount++;

                    // PREPARE FOR NEXT SEGMENT
                    // The end of this segment is the start of the next.
                    long overlapNodeID = segmentNodeIDs[segmentNodeIDs.Count - 1];
                    segmentNodeIDs.Clear();
                    segmentNodeIDs.Add(overlapNodeID);
                }
            }
        }
        Debug.Log($"Generated {spawnedNodeMap.Count} Nodes and {segmentCount} Road Segments.");
    }

    void CreateSegmentObject(List<long> ids, Transform parent, Way originalWay)
    {
        GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(segmentPrefab, parent);
        obj.name = $"Segment_{ids[0]}_{ids[ids.Count-1]}";

        // 1. Build Spline
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

        // 2. Link Logic
        RoadSegment segmentScript = obj.GetComponent<RoadSegment>();
        
        // Find Start Node
        if (spawnedNodeMap.TryGetValue(ids[0], out RoadNode startNode))
        {
            segmentScript.StartNode = startNode;
            startNode.OutgoingRoads.Add(segmentScript); // Bi-directional? handled later
        }

        // Find End Node
        if (spawnedNodeMap.TryGetValue(ids[ids.Count - 1], out RoadNode endNode))
        {
            segmentScript.EndNode = endNode;
            // Note: If road is two-way, you might need logic here to add connection to EndNode too
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