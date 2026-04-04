using System.Collections.Generic;
using UnityEngine;

// 1. Define a struct to hold the Prefab and its Weight in the Inspector
[System.Serializable]
public struct AmbientVehicleSpawnDef
{
    public AmbientVehicle vehiclePrefab;
    [Tooltip("Higher number means it spawns more often relative to others.")]
    [Range(1, 100)] public int spawnWeight;
}

[DefaultExecutionOrder(50)] 
public class AmbientTrafficManager : MonoBehaviour
{
    [Header("Spawning Configuration")]
    // 2. Replace the single prefab with a list of our new struct
    public List<AmbientVehicleSpawnDef> vehicleTypes = new List<AmbientVehicleSpawnDef>();
    public Transform cameraTarget; 
    public int maxVehicles = 100;
    
    [Header("Radius Settings")]
    public float innerSpawnRadius = 60f;
    public float outerDespawnRadius = 120f;

    private RoadSegment[] _allRoads;
    private Queue<AmbientVehicle> _vehiclePool = new Queue<AmbientVehicle>();
    private List<AmbientVehicle> _activeVehicles = new List<AmbientVehicle>();

    private void Start()
    {
        _allRoads = FindObjectsByType<RoadSegment>(FindObjectsSortMode.None);
        
        if (cameraTarget == null && Camera.main != null)
            cameraTarget = Camera.main.transform;

        InitializeWeightedPool();
    }

    private void InitializeWeightedPool()
    {
        if (vehicleTypes == null || vehicleTypes.Count == 0)
        {
            Debug.LogError("[AmbientTrafficManager] No vehicle types assigned!");
            return;
        }

        List<AmbientVehicle> initialSpawnList = new List<AmbientVehicle>();
        
        // Calculate total weight
        int totalWeight = 0;
        foreach (var def in vehicleTypes) totalWeight += def.spawnWeight;

        int spawnedCount = 0;

        // Instantiate the exact number of each prefab based on their weight
        for (int i = 0; i < vehicleTypes.Count; i++)
        {
            var def = vehicleTypes[i];
            if (def.vehiclePrefab == null) continue;

            // Calculate ratio
            int amountToSpawn = Mathf.RoundToInt(((float)def.spawnWeight / totalWeight) * maxVehicles);

            // If it's the very last item, fill whatever is left to ensure we hit exactly maxVehicles
            if (i == vehicleTypes.Count - 1)
            {
                amountToSpawn = maxVehicles - spawnedCount;
            }

            for (int j = 0; j < amountToSpawn; j++)
            {
                if (spawnedCount >= maxVehicles) break;
                
                AmbientVehicle newVeh = Instantiate(def.vehiclePrefab, transform);
                newVeh.Initialize();
                newVeh.gameObject.SetActive(false);
                
                initialSpawnList.Add(newVeh);
                spawnedCount++;
            }
        }

        // Shuffle the list so they don't spawn in clumps of the same type
        ShuffleList(initialSpawnList);

        // Enqueue them all
        foreach (var veh in initialSpawnList)
        {
            _vehiclePool.Enqueue(veh);
        }
    }

    // A fast Fisher-Yates shuffle
    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            T temp = list[i];
            int randomIndex = Random.Range(i, list.Count);
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    private void Update()
    {
        if (cameraTarget == null) return;

        ManageSpawning();
        UpdateActiveVehicles();
    }

    private void ManageSpawning()
    {
        if (_vehiclePool.Count > 0 && _activeVehicles.Count < maxVehicles)
        {
            TrySpawnVehicle();
        }
    }

    private void TrySpawnVehicle()
    {
        if (_allRoads.Length == 0) return;

        // Shuffle the roads to keep spawns organic, but iterate through them 
        // to GUARANTEE we find a valid road if one exists near the camera.
        int startIndex = Random.Range(0, _allRoads.Length);
        
        for (int i = 0; i < _allRoads.Length; i++)
        {
            int index = (startIndex + i) % _allRoads.Length;
            RoadSegment checkRoad = _allRoads[index];

            // Use the Node position, as Spline pivots can lie and cause instant-despawns
            Vector3 roadPos = checkRoad.NodeA != null ? checkRoad.NodeA.transform.position : checkRoad.transform.position;
            float distToCam = Vector3.Distance(roadPos, GetGroundCameraPosition());

            if (distToCam > innerSpawnRadius && distToCam < outerDespawnRadius)
            {
                if (GridManager.Instance != null)
                {
                    if (GridManager.Instance.WorldToGrid(roadPos, out int x, out int y))
                    {
                        TileData tile = GridManager.Instance.GetTileData(x, y);
                        int spawnChance = Mathf.Max(5, tile.Traffic);

                        if (Random.Range(0, 100) > spawnChance)
                        {
                            continue; // Failed traffic check, keep searching
                        }
                    }
                }

                SpawnFromPool(checkRoad);
                return; // Spawned successfully, exit the loop
            }
        }
    }

    private void SpawnFromPool(RoadSegment startSegment)
    {
        AmbientVehicle veh = _vehiclePool.Dequeue();
        veh.gameObject.SetActive(true);
        veh.SpawnReset();

        veh.CurrentSegment = startSegment;
        veh.IsHeadingToNodeB = Random.value > 0.5f;
        veh.DistanceTraveledOnSegment = 0f;
        
        float evalT = veh.IsHeadingToNodeB ? 0f : 1f;
        veh.transform.position = startSegment.GetPointOnRoad(evalT, veh.IsHeadingToNodeB);
        veh.IsActive = true;
        
        _activeVehicles.Add(veh);
    }

    private void UpdateActiveVehicles()
    {
        Vector3 camPos = GetGroundCameraPosition();
        float dt = Time.deltaTime;

        for (int i = _activeVehicles.Count - 1; i >= 0; i--)
        {
            AmbientVehicle veh = _activeVehicles[i];

            if (Vector3.Distance(veh.transform.position, camPos) > outerDespawnRadius && !veh.IsDespawning)
            {
                veh.TriggerDespawnFade();
            }

            bool actionRequired = veh.CustomUpdate(dt);

            if (actionRequired)
            {
                if (veh.IsDespawning)
                {
                    ReturnToPool(veh, i);
                }
                else
                {
                    PickNextNode(veh);
                }
            }
        }
    }

    private void PickNextNode(AmbientVehicle veh)
    {
        RoadNode arrivalNode = veh.IsHeadingToNodeB ? veh.CurrentSegment.NodeB : veh.CurrentSegment.NodeA;
        
        if (arrivalNode == null || arrivalNode.ConnectedRoads == null || arrivalNode.ConnectedRoads.Count <= 1)
        {
            veh.TriggerDespawnFade();
            return; 
        }

        RoadSegment nextSegment = null;
        int startIndex = Random.Range(0, arrivalNode.ConnectedRoads.Count);
        for (int i = 0; i < arrivalNode.ConnectedRoads.Count; i++)
        {
            int index = (startIndex + i) % arrivalNode.ConnectedRoads.Count;
            RoadSegment potentialSeg = arrivalNode.ConnectedRoads[index];
            
            if (potentialSeg != veh.CurrentSegment)
            {
                nextSegment = potentialSeg;
                break;
            }
        }

        if (nextSegment != null)
        {
            veh.CurrentSegment = nextSegment;
            veh.IsHeadingToNodeB = nextSegment.IsHeadingToNodeB(arrivalNode);
            veh.DistanceTraveledOnSegment = 0f; 
        }
        else
        {
            veh.TriggerDespawnFade();
        }
    }

    private void ReturnToPool(AmbientVehicle veh, int listIndex)
    {
        veh.IsActive = false;
        veh.gameObject.SetActive(false);
        _activeVehicles.RemoveAt(listIndex);
        _vehiclePool.Enqueue(veh);
    }

    private Vector3 GetGroundCameraPosition()
    {
        return new Vector3(cameraTarget.position.x, 0f, cameraTarget.position.z);
    }
}