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
    private string _osmFilePath = "";
    private float _mapScale = 1.0f;
    private GameObject _nodePrefab;     
    private GameObject _segmentPrefab;  
    private Dictionary<long, Vector3> _relevantNodePositions = new Dictionary<long, Vector3>();
    private List<Way> _highwayWays = new List<Way>();
    private HashSet<long> _intersectionIDs = new HashSet<long>();
    private double _originLat = 0;
    private double _originLon = 0;
    private bool _hasOrigin = false;
    private Dictionary<long, RoadNode> _spawnedNodeMap = new Dictionary<long, RoadNode>();
    private const string PREF_PATH = "OSM_FilePath";
    private const string PREF_SCALE = "OSM_Scale";

    [MenuItem("Tools/OSM Road Importer")]
    public static void ShowWindow()
    {
        GetWindow<OSMImporterWindow>("OSM Importer");
    }

    private void OnEnable()
    {
        _osmFilePath = EditorPrefs.GetString(PREF_PATH, "Assets/map.osm");
        _mapScale = EditorPrefs.GetFloat(PREF_SCALE, 1.0f);
    }

    private void OnDisable()
    {
        SaveSettings();
    }

    private void SaveSettings()
    {
        EditorPrefs.SetString(PREF_PATH, _osmFilePath);
        EditorPrefs.SetFloat(PREF_SCALE, _mapScale);
    }

    private void OnGUI()
    {
        GUILayout.Label("OSM Import Settings", EditorStyles.boldLabel);

        // File Selection
        GUILayout.BeginHorizontal();
        string newPath = EditorGUILayout.TextField("File Path", _osmFilePath);
        if (newPath != _osmFilePath) { _osmFilePath = newPath; SaveSettings(); }
        
        if (GUILayout.Button("Browse", GUILayout.Width(75)))
        {
            string selected = EditorUtility.OpenFilePanel("Select OSM/PBF File", Application.dataPath, "pbf,osm");
            if (!string.IsNullOrEmpty(selected)) 
            {
                // Make relative to project if possible
                if (selected.StartsWith(Application.dataPath)) 
                    _osmFilePath = "Assets" + selected.Substring(Application.dataPath.Length);
                else 
                    _osmFilePath = selected;
                    
                SaveSettings();
            }
        }
        GUILayout.EndHorizontal();

        if (_nodePrefab == null) _nodePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RoadNode.prefab"); 
        
        _nodePrefab = (GameObject)EditorGUILayout.ObjectField("Intersection Prefab", _nodePrefab, typeof(GameObject), false);
        _segmentPrefab = (GameObject)EditorGUILayout.ObjectField("Road Segment Prefab", _segmentPrefab, typeof(GameObject), false);

        float newScale = EditorGUILayout.FloatField("Scale Factor", _mapScale);
        if (newScale != _mapScale) { _mapScale = newScale; SaveSettings(); }

        GUILayout.Space(10);

        if (GUILayout.Button("GENERATE MAP", GUILayout.Height(40)))
        {
            if (CheckReferences()) RunImportProcess();
        }
    }

    private bool CheckReferences()
    {
        if (!File.Exists(_osmFilePath)) { EditorUtility.DisplayDialog("Error", "File not found!", "OK"); return false; }
        if (_nodePrefab == null || _segmentPrefab == null) { EditorUtility.DisplayDialog("Error", "Assign Prefabs first!", "OK"); return false; }
        return true;
    }

    private void RunImportProcess()
    {
        ClearInternalData();
        ReadWaysPass();
        ReadNodesPass();
        SpawnNetwork();
        Debug.Log("OSM Import Complete.");
    }

    private void ReadWaysPass()
    {
        Dictionary<long, int> nodeUsageCount = new Dictionary<long, int>();

        using (var fileStream = File.OpenRead(_osmFilePath))
        {
            var source = GetStreamSource(fileStream);
            
            HashSet<string> allowedTypes = new HashSet<string> 
            { 
                "motorway", "trunk", "primary", "secondary", "tertiary", "unclassified", "residential"
            };

            var highways = source.Where(x => 
                x.Type == OsmGeoType.Way && 
                x.Tags != null && 
                x.Tags.ContainsKey("highway") &&
                allowedTypes.Contains(x.Tags.GetValue("highway"))
            );

            foreach (var element in highways)
            {
                Way w = (Way)element;
                if (w.Nodes == null || w.Nodes.Length < 2) continue;

                _highwayWays.Add(w);

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
            bool isJunction = kvp.Value > 1;
            bool isTerminal = false;
            
            if (!isJunction) 
            {
                foreach(var way in _highwayWays)
                {
                    if (way.Nodes[0] == kvp.Key || way.Nodes[way.Nodes.Length-1] == kvp.Key)
                    {
                        isTerminal = true;
                        break;
                    }
                }
            }

            if (isJunction || isTerminal)
            {
                _intersectionIDs.Add(kvp.Key);
            }
        }
    }

    private void ReadNodesPass()
    {
        HashSet<long> neededNodes = new HashSet<long>();
        foreach(var way in _highwayWays)
        {
            foreach(var id in way.Nodes) neededNodes.Add(id);
        }

        using (var fileStream = File.OpenRead(_osmFilePath))
        {
            var source = GetStreamSource(fileStream);
            
            foreach (var element in source)
            {
                if (element.Type == OsmGeoType.Node)
                {
                    if (neededNodes.Contains(element.Id.Value))
                    {
                        OsmSharp.Node n = (OsmSharp.Node)element;
                        if (!n.Latitude.HasValue || !n.Longitude.HasValue) continue;

                        if (!_hasOrigin)
                        {
                            _originLat = n.Latitude.Value;
                            _originLon = n.Longitude.Value;
                            _hasOrigin = true;
                        }

                        Vector3 worldPos = GeoToWorld(n.Latitude.Value, n.Longitude.Value);
                        _relevantNodePositions.Add(element.Id.Value, worldPos);
                    }
                }
            }
        }
    }

    private OsmStreamSource GetStreamSource(FileStream stream)
    {
        if (_osmFilePath.EndsWith(".pbf")) return new PBFOsmStreamSource(stream);
        else return new XmlOsmStreamSource(stream);
    }

    private void SpawnNetwork()
    {
        GameObject root = new GameObject("OSM_Generated_Map");
        
        RoadNetwork networkScript = root.AddComponent<RoadNetwork>();
        networkScript.nodePrefab = this._nodePrefab; 
        networkScript.showDebugGizmos = false;

        GameObject intersectionsGroup = new GameObject("Nodes");
        GameObject segmentsGroup = new GameObject("Roads");
        intersectionsGroup.transform.parent = root.transform;
        segmentsGroup.transform.parent = root.transform;

        foreach (long id in _intersectionIDs)
        {
            if (!_relevantNodePositions.ContainsKey(id)) continue;

            Vector3 pos = _relevantNodePositions[id];
            
            GameObject nodeObj = (GameObject)PrefabUtility.InstantiatePrefab(_nodePrefab, intersectionsGroup.transform);
            nodeObj.transform.position = pos;
            nodeObj.name = $"Node_{id}";

            RoadNode nodeScript = nodeObj.GetComponent<RoadNode>();
            nodeScript.OSM_NodeID = id;
            
            _spawnedNodeMap.Add(id, nodeScript);
        }

        foreach (Way way in _highwayWays)
        {
            List<long> segmentNodeIDs = new List<long>();
            for (int i = 0; i < way.Nodes.Length; i++)
            {
                long currentNodeID = way.Nodes[i];
                segmentNodeIDs.Add(currentNodeID);

                bool isIntersection = _intersectionIDs.Contains(currentNodeID);
                bool isLastInWay = (i == way.Nodes.Length - 1);
                bool hasGeometry = segmentNodeIDs.Count > 1;

                if (hasGeometry && (isIntersection || isLastInWay))
                {
                    CreateSegmentObject(segmentNodeIDs, segmentsGroup.transform, way);
                    
                    long overlapNodeID = segmentNodeIDs[segmentNodeIDs.Count - 1];
                    segmentNodeIDs.Clear();
                    segmentNodeIDs.Add(overlapNodeID);
                }
            }
        }
    }

    private void CreateSegmentObject(List<long> ids, Transform parent, Way originalWay)
    {
        GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(_segmentPrefab, parent);
        obj.name = $"Segment_{ids[0]}_{ids[ids.Count-1]}";

        obj.transform.position = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;

        SplineContainer splineContainer = obj.GetComponent<SplineContainer>();
        Spline spline = splineContainer.Spline;
        spline.Clear();

        foreach (long id in ids)
        {
            if (_relevantNodePositions.TryGetValue(id, out Vector3 pos))
            {
                spline.Add(new BezierKnot(pos));
            }
        }
        spline.SetTangentMode(TangentMode.AutoSmooth);

        RoadSegment segmentScript = obj.GetComponent<RoadSegment>();
        
        if (_spawnedNodeMap.TryGetValue(ids[0], out RoadNode nodeA))
        {
            segmentScript.NodeA = nodeA;
            nodeA.ConnectedRoads.Add(segmentScript);
        }

        if (_spawnedNodeMap.TryGetValue(ids[ids.Count - 1], out RoadNode nodeB))
        {
            segmentScript.NodeB = nodeB;
            nodeB.ConnectedRoads.Add(segmentScript);
        }

        segmentScript.CalculateLength();
    }

    #region helpers

    private Vector3 GeoToWorld(double lat, double lon)
    {
        double R = 6378137; // Earth Radius meters
        double dLat = (lat - _originLat) * Mathf.Deg2Rad;
        double dLon = (lon - _originLon) * Mathf.Deg2Rad;
        
        float x = (float)(dLon * System.Math.Cos(_originLat * Mathf.Deg2Rad) * R);
        float z = (float)(dLat * R);

        return new Vector3(x * _mapScale, 0, z * _mapScale);
    }

    private void ClearInternalData()
    {
        _relevantNodePositions.Clear();
        _highwayWays.Clear();
        _intersectionIDs.Clear();
        _spawnedNodeMap.Clear();
        _hasOrigin = false;
    }
    #endregion
}