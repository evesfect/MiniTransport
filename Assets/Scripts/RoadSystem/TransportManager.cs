using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Unity.Netcode;
using Unity.VisualScripting;
using System.ComponentModel;
using System;


#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-50)] 
public class TransportManager : NetworkBehaviour
{
    public static TransportManager Instance { get; private set; }

    [Header("Registry")]
    [SerializeField] private List<BusStop> _debugStopList = new List<BusStop>();
    private Dictionary<string, BusStop> _stopRegistry = new Dictionary<string, BusStop>();

    [Header("Routes")]
    public List<Route> ActiveRoutes = new List<Route>();

    public event Action OnRoutesChanged;

    // Local cache
    private Dictionary<string, List<RoadNode>> _pathCache = new Dictionary<string, List<RoadNode>>();
    
    public enum RouteOperation { Add, Update, Remove}

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
    }

    public override void OnNetworkSpawn()
    {
        RegisterAllStops();

        if (IsServer)
        {
            LoadRoutes(); // server loads master data
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected; // sync new clients
        }
        else
        {
            // clear stale states
            ActiveRoutes.Clear();
            // _pathCache.Clear(); // the scene roads are static, so can use existing cache
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            // Sync ONLY to the new client
            string json = SerializeRoutes();
            SyncRoutesRpc(json, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }
    private void OnApplicationQuit()
    {
        if (IsServer) SaveRoutes();
    }

    /// <summary>
    /// Server calls this to sync client-local routes with the json string sent
    /// </summary>
    /// <param name="jsonRoutes"></param>
    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
    private void SyncRoutesRpc(string jsonRoutes, RpcParams rpcParams = default)
    {
        RouteContainer container = JsonUtility.FromJson<RouteContainer>(jsonRoutes);
        if (container != null && container.Routes != null)
        {
            ActiveRoutes = container.Routes;
            Debug.Log($"[TransportManager] Synced {ActiveRoutes.Count} routes from Server.");
            OnRoutesChanged?.Invoke();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestRouteOperationRpc(string routeJson, RouteOperation op)
    {
        Route requestRoute = JsonUtility.FromJson<Route>(routeJson);
        if (requestRoute == null) return;

        bool changed = false;

        switch (op)
        {
            case RouteOperation.Add:
                if (!ActiveRoutes.Any(r => r.RouteID == requestRoute.RouteID))
                {
                    ActiveRoutes.Add(requestRoute);
                    changed = true;
                }
                break;
            case RouteOperation.Update:
                int idx = ActiveRoutes.FindIndex(r => r.RouteID == requestRoute.RouteID);
                if (idx != -1)
                {
                    ActiveRoutes[idx] = requestRoute;
                    changed = true;
                }
                break;
            case RouteOperation.Remove:
                Route toRemove = ActiveRoutes.FirstOrDefault(r => r.RouteID == requestRoute.RouteID);
                if (toRemove != null)
                {
                    ActiveRoutes.Remove(toRemove);
                    changed = true;
                }
                break;
        }

        if (changed)
        {
            SaveRoutes(); // persist on server
            string fullJson = SerializeRoutes();
            SyncRoutesRpc(fullJson); // broadcast to all clients

            Debug.Log($"[TransportManager] Route Operation {op} applied for {requestRoute.RouteName}");
            OnRoutesChanged?.Invoke();
        }
    }

    // Public API for clients

    public void AddRouteClient(Route route)
    {
        string json = JsonUtility.ToJson(route);
        RequestRouteOperationRpc(json, RouteOperation.Add);
    }
    public void UpdateRouteClient(Route route)
    {
        string json = JsonUtility.ToJson(route);
        RequestRouteOperationRpc(json, RouteOperation.Update);
    }
    public void DeleteRouteClient(Route route)
    {
        string json = JsonUtility.ToJson(route);
        RequestRouteOperationRpc(json, RouteOperation.Remove);
    }

    // Local Logic Below
    public void RegisterAllStops()
    {
        _stopRegistry.Clear();
        _debugStopList.Clear();
        var stops = FindObjectsByType<BusStop>(FindObjectsSortMode.None);
        
        foreach (var stop in stops)
        {
            if (string.IsNullOrEmpty(stop.stopID)) 
            {
                continue;
            }
            if (!_stopRegistry.ContainsKey(stop.stopID))
            {
                _stopRegistry.Add(stop.stopID, stop);
                _debugStopList.Add(stop);
            }
        }
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
        if (start.parentSegment.NodeA == null || start.parentSegment.NodeB == null) return null;
        if (end.parentSegment.NodeA == null || end.parentSegment.NodeB == null) return null;

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

    public Route GetRoute(string routeID)
    {
        return ActiveRoutes.FirstOrDefault(r => r.RouteID == routeID);
    }

    // KPI: % of registered stops served by at least one active route. -1 if no stops exist.
    public float StopCoveragePercent()
    {
        if (_stopRegistry == null || _stopRegistry.Count == 0) return -1f;
        return (ServedRegisteredStops() / (float)_stopRegistry.Count) * 100f;
    }

    // KPI: number of registered stops not served by any active route.
    public int StopsNotCovered()
    {
        if (_stopRegistry == null) return 0;
        return Mathf.Max(0, _stopRegistry.Count - ServedRegisteredStops());
    }

    // KPI: highest stop count on any single active route (0 if no routes).
    public int LongestRouteStopCount()
    {
        int max = 0;
        foreach (var route in ActiveRoutes)
            if (route?.StopIDs != null && route.StopIDs.Count > max)
                max = route.StopIDs.Count;
        return max;
    }

    // Count of registered stops that appear on at least one active route.
    private int ServedRegisteredStops()
    {
        var served = new HashSet<string>();
        foreach (var route in ActiveRoutes)
        {
            if (route?.StopIDs == null) continue;
            foreach (var stopID in route.StopIDs)
                if (_stopRegistry.ContainsKey(stopID)) served.Add(stopID);
        }
        return served.Count;
    }

    private string SerializeRoutes()
    {
        RouteContainer container = new RouteContainer { Routes = ActiveRoutes };
        return JsonUtility.ToJson(container);
    }

    private void SaveRoutes()
    {
        string json = SerializeRoutes();
        File.WriteAllText(SavePath, json);
    }

    private void LoadRoutes()
    {
        if (File.Exists(SavePath))
        {
            string json = File.ReadAllText(SavePath);
            RouteContainer container = JsonUtility.FromJson<RouteContainer>(json);
            if (container != null && container.Routes != null)
            {
                ActiveRoutes = container.Routes;
            }
        }
    }

    public Route CreateRouteLocal(string routeName, Color color)
    {
        Route newRoute = new Route(routeName, new List<string>(), color);
        ActiveRoutes.Add(newRoute);
        return newRoute;
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