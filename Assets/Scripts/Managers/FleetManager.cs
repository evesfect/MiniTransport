using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[DefaultExecutionOrder(-50)] // Init after TimeManager, but before DepotController
public class FleetManager : MonoBehaviour
{
    public static FleetManager Instance { get; private set; }

    [Header("Master Fleet Data")]
    public List<DepotBusEntry> allBuses = new List<DepotBusEntry>();

    // persistentDataPath for builds, dataPath for Editor visibility
#if UNITY_EDITOR
    private string SavePath => Path.Combine(Application.dataPath, "fleet.json");
#else
    private string SavePath => Path.Combine(Application.persistentDataPath, "fleet.json");
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        LoadFleet();
    }

    private void OnApplicationQuit()
    {
        SaveFleet();
    }

    /// <summary>
    /// Returns all buses assigned to the specified Depot ID.
    /// </summary>
    public List<DepotBusEntry> GetBusesForDepot(string depotID)
    {
        return allBuses.Where(b => b.AssignedDepotID == depotID).ToList();
    }

    /// <summary>
    /// Adds a new bus to the fleet and saves.
    /// </summary>
    public void CreateBus(string busID, string depotID, BusSchedule schedule)
    {
        DepotBusEntry newBus = new DepotBusEntry
        {
            BusID = busID,
            AssignedDepotID = depotID,
            Schedule = schedule,
            CurrentState = BusState.InDepot
        };

        allBuses.Add(newBus);
        SaveFleet();
    }

    public void DeleteBus(DepotBusEntry entry)
    {
        if (allBuses.Contains(entry))
        {
            allBuses.Remove(entry);
            SaveFleet();
        }
    }

    // --- Persistence ---

    [ContextMenu("Save Fleet")]
    public void SaveFleet()
    {
        FleetContainer container = new FleetContainer { Buses = allBuses };
        string json = JsonUtility.ToJson(container, true);
        File.WriteAllText(SavePath, json);
        Debug.Log($"FleetManager: Saved {allBuses.Count} buses to {SavePath}");
    }

    [ContextMenu("Load Fleet")]
    public void LoadFleet()
    {
        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);
                FleetContainer container = JsonUtility.FromJson<FleetContainer>(json);
                if (container != null && container.Buses != null)
                {
                    allBuses = container.Buses;
                    
                    // Reset runtime states on load (crucial for ensuring they start 'InDepot')
                    foreach(var bus in allBuses) 
                    {
                        bus.ActiveBusInstance = null;
                        bus.CurrentState = BusState.InDepot; 
                    }
                    Debug.Log($"FleetManager: Loaded {allBuses.Count} buses.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"FleetManager: Failed to load fleet.json. Error: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"FleetManager: No fleet file found at {SavePath}");
        }
    }
}

[System.Serializable]
public class FleetContainer
{
    public List<DepotBusEntry> Buses;
}