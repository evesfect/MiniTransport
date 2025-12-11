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
            if (string.IsNullOrEmpty(stop.stopID)) stop.stopID = System.Guid.NewGuid().ToString().Substring(0, 8);
            
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

        // "Bus has to follow the direction it entered the bus stop"
        // We track the node we are heading towards as we leave a stop.
        // For Spawn (First Stop), we default to NodeB (Forward).
        
        BusStop firstStop = GetStop(route.StopIDs[0]);
        if (firstStop == null || firstStop.parentSegment == null) return 0;

        // Default start direction: We leave the first stop via NodeB (Forward)
        RoadNode searchStartNode = firstStop.parentSegment.NodeB;

        for (int i = 0; i < route.StopIDs.Count - 1; i++)
        {
            BusStop start = GetStop(route.StopIDs[i]);
            BusStop end = GetStop(route.StopIDs[i + 1]);

            if (start != null && end != null)
            {
                string key = start.stopID + "_" + end.stopID;
                
                // Even if cached, we must update 'searchStartNode' for the next loop logic
                // But for simplicity in this prompt, we calculate fresh if not found.
                if (!_pathCache.ContainsKey(key))
                {
                    // 1. Run Pathfinder ONCE
                    List<RoadNode> path = RoadPathfinder.FindPathToSegment(searchStartNode, end.parentSegment);
                    
                    if (path != null && path.Count > 0)
                    {
                        _pathCache[key] = path;
                        cachedCount++;

                        // 2. Determine Next Start Node (Continuity)
                        // The path ends at one of the EndSegment's nodes (A or B).
                        // That node is where we ENTER the EndSegment.
                        // We must TRAVERSE the segment to the other node to EXIT it.
                        RoadNode entryNode = path.Last();
                        
                        // If we entered at A, we leave via B. If we entered at B, we leave via A.
                        searchStartNode = (entryNode == end.parentSegment.NodeA) ? 
                                           end.parentSegment.NodeB : 
                                           end.parentSegment.NodeA;
                    }
                    else
                    {
                        Debug.LogWarning($"No path found from {start.name} to {end.name} starting towards {searchStartNode.name}");
                        // Break continuity if path fails, maybe reset to NodeB?
                        if(end.parentSegment) searchStartNode = end.parentSegment.NodeB; 
                    }
                }
                else
                {
                    // If cached, we still need to update searchStartNode for the next iteration logic
                    List<RoadNode> existingPath = _pathCache[key];
                    RoadNode entryNode = existingPath.Last();
                    searchStartNode = (entryNode == end.parentSegment.NodeA) ? end.parentSegment.NodeB : end.parentSegment.NodeA;
                }
            }
        }
        return cachedCount;
    }

    public List<RoadNode> GetCachedPath(BusStop start, BusStop end)
    {
        if (start == null || end == null) return null;
        string key = start.stopID + "_" + end.stopID;
        return _pathCache.TryGetValue(key, out var p) ? p : null;
    }

    private string GetCacheKey(BusStop a, BusStop b) => a.stopID + "_" + b.stopID;



    public BusStop GetStop(string stopID)
    {
        if (string.IsNullOrEmpty(stopID)) return null;
        return _stopRegistry.TryGetValue(stopID, out BusStop stop) ? stop : null;
    }

    // --- Route Management ---

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
    
    // --- Persistence ---

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