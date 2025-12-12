using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-50)] 
public class TransportManager : MonoBehaviour
{
    public static TransportManager Instance { get; private set; }

    [Header("Registry")]
    [SerializeField] private List<BusStop> _debugStopList = new List<BusStop>();
    private Dictionary<string, BusStop> _stopRegistry = new Dictionary<string, BusStop>();

    [Header("Routes")]
    public List<Route> ActiveRoutes = new List<Route>();
    private Dictionary<string, List<RoadNode>> _pathCache = new Dictionary<string, List<RoadNode>>();
    
    private string SavePath
    {
        get
        {
#if UNITY_EDITOR
            // Save to Assets/routes.json
            return Path.Combine(Application.dataPath, "routes.json");
#else
            // Save to AppData (standard for builds)
            return Path.Combine(Application.persistentDataPath, "routes.json");
#endif
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        RegisterAllStops();
        LoadRoutes(); // triggers path calculation
    }

    private void OnApplicationQuit()
    {
        SaveRoutes();
    }

    public void RegisterAllStops()
    {
        _stopRegistry.Clear();
        _debugStopList.Clear();
        var stops = FindObjectsByType<BusStop>(FindObjectsSortMode.None);
        
        foreach (var stop in stops)
        {
            if (string.IsNullOrEmpty(stop.stopID)) 
            {
                Debug.LogError($"[TransportManager] Stop '{stop.name}' has NO ID! Select it in editor to generate one.");
                continue;
            }
            if (!_stopRegistry.ContainsKey(stop.stopID))
            {
                _stopRegistry.Add(stop.stopID, stop);
                _debugStopList.Add(stop);
            }
        }
        Debug.Log($"TransportManager: Indexed {_stopRegistry.Count} bus stops.");
    }

    public void RecalculateAllPaths()
    {
        _pathCache.Clear();
        int count = 0;
        foreach (var route in ActiveRoutes) count += CacheRoute(route);
        Debug.Log($"TransportManager: Cached {count} legs.");
    }

    public int CacheRoute(Route route)
    {
        if (route.StopIDs.Count < 2) return 0;
        int cachedCount = 0;

        // Bus has to follow the direction it entered the stop from
        // Track the node bus is heading towards as it leaves a stop.
        // For Spawn (First Stop), default to NodeB (Forward).
        
        BusStop firstStop = GetStop(route.StopIDs[0]);
        if (firstStop == null || firstStop.parentSegment == null) return 0;

        // Default start direction: Leave the first stop via NodeB (Forward)
        RoadNode searchStartNode = firstStop.parentSegment.NodeB;

        for (int i = 0; i < route.StopIDs.Count - 1; i++)
        {
            BusStop start = GetStop(route.StopIDs[i]);
            BusStop end = GetStop(route.StopIDs[i + 1]);

            if (start != null && end != null)
            {
                string key = start.stopID + "_" + end.stopID;
                
                if (!_pathCache.ContainsKey(key))
                {
                    List<RoadNode> path = RoadPathfinder.FindPathToSegment(searchStartNode, end.parentSegment);
                    
                    if (path != null && path.Count > 0)
                    {
                        _pathCache[key] = path;
                        cachedCount++;

                        RoadNode entryNode = path.Last();
                        
                        searchStartNode = (entryNode == end.parentSegment.NodeA) ? 
                                           end.parentSegment.NodeB : 
                                           end.parentSegment.NodeA;
                    }
                    else
                    {
                        Debug.LogWarning($"No path found from {start.name} to {end.name} starting towards {searchStartNode.name}");
                        if(end.parentSegment) searchStartNode = end.parentSegment.NodeB; 
                    }
                }
                else
                {
                    List<RoadNode> existingPath = _pathCache[key];
                    RoadNode entryNode = existingPath.Last();
                    searchStartNode = (entryNode == end.parentSegment.NodeA) ? end.parentSegment.NodeB : end.parentSegment.NodeA;
                }
            }
        }
        return cachedCount;
    }

    public List<RoadNode> GetPath(BusStop start, BusStop end)
    {
        if (start == null || end == null) return null;
        string key = GetCacheKey(start, end);
        if (_pathCache.TryGetValue(key, out var cachedPath))
        {
            return cachedPath;
        }
        if (start.parentSegment == null || end.parentSegment == null) return null;

        // Path not cached, calculate
        RoadNode searchStart = start.parentSegment.NodeB; 
        List<RoadNode> newPath = RoadPathfinder.FindPathToSegment(searchStart, end.parentSegment);

        if (newPath == null)
        {
             // Try the other direction (NodeA)
             searchStart = start.parentSegment.NodeA;
             newPath = RoadPathfinder.FindPathToSegment(searchStart, end.parentSegment);
        }

        if (newPath != null)
        {
            _pathCache[key] = newPath;
            return newPath;
        }

        Debug.LogWarning($"TransportManager: Could not calculate path on-demand from {start.name} to {end.name}");
        return null;
    }

    private string GetCacheKey(BusStop a, BusStop b) => a.stopID + "_" + b.stopID;



    public BusStop GetStop(string stopID)
    {
        if (string.IsNullOrEmpty(stopID)) return null;
        return _stopRegistry.TryGetValue(stopID, out BusStop stop) ? stop : null;
    }

    // Route Management

    // 1. For Game UI (Empty Route)
    public Route CreateRoute(string routeName, Color color)
    {
        Route newRoute = new Route(routeName, new List<string>(), color);
        ActiveRoutes.Add(newRoute);
        return newRoute;
    }

    // 2. For Debugger / Loader (Pre-filled)
    public Route CreateRoute(string routeName, List<string> stopIDs, Color color)
    {
        Route newRoute = new Route(routeName, stopIDs, color);
        ActiveRoutes.Add(newRoute);
        return newRoute;
    }

    public void DeleteRoute(Route route)
    {
        if (ActiveRoutes.Contains(route))
        {
            ActiveRoutes.Remove(route);
            SaveRoutes();
        }
    }
    
    // Persistence

    [ContextMenu("Save Routes")]
    public void SaveRoutes()
    {
        RouteContainer container = new RouteContainer { Routes = ActiveRoutes };
        string json = JsonUtility.ToJson(container, true);
        File.WriteAllText(SavePath, json);
        Debug.Log($"Routes saved to {SavePath}");

#if UNITY_EDITOR
        // Refresh the Project window so the file appears instantly
        AssetDatabase.Refresh();
#endif
    }

    [ContextMenu("Load Routes")]
    public void LoadRoutes()
    {
        if (File.Exists(SavePath))
        {
            string json = File.ReadAllText(SavePath);
            RouteContainer container = JsonUtility.FromJson<RouteContainer>(json);
            if (container != null && container.Routes != null)
            {
                ActiveRoutes = container.Routes;
                Debug.Log($"Loaded {ActiveRoutes.Count} routes from {SavePath}");
            }
            RecalculateAllPaths();
        }
        else
        {
            Debug.LogWarning("No routes file found at " + SavePath);
        }
    }
}

[System.Serializable]
public class RouteContainer
{
    public List<Route> Routes;
}

[System.Serializable]
public class Route
{
    public string RouteID;
    public string RouteName;
    public List<string> StopIDs;
    public Color RouteColor;

    public Route(string name, List<string> stops, Color color)
    {
        RouteID = System.Guid.NewGuid().ToString().Substring(0, 8);
        RouteName = name;
        StopIDs = new List<string>(stops);
        RouteColor = color;
    }
}