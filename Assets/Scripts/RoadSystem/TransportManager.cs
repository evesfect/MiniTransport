using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;

[DefaultExecutionOrder(-50)] 
public class TransportManager : MonoBehaviour
{
    public static TransportManager Instance { get; private set; }

    [Header("Registry")]
    [SerializeField] private List<BusStop> _debugStopList = new List<BusStop>();
    private Dictionary<string, BusStop> _stopRegistry = new Dictionary<string, BusStop>();

    [Header("Routes")]
    public List<Route> ActiveRoutes = new List<Route>();

    private string SavePath => Path.Combine(Application.persistentDataPath, "routes.json");

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
        LoadRoutes(); // Load on startup
    }

    private void OnApplicationQuit()
    {
        SaveRoutes(); // Auto-save on exit
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

    public BusStop GetStop(string stopID)
    {
        if (string.IsNullOrEmpty(stopID)) return null;
        return _stopRegistry.TryGetValue(stopID, out BusStop stop) ? stop : null;
    }

    // --- Route Management ---

    public Route CreateRoute(string routeName, Color color)
    {
        Route newRoute = new Route(routeName, new List<string>(), color);
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
                Debug.Log($"Loaded {ActiveRoutes.Count} routes.");
            }
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