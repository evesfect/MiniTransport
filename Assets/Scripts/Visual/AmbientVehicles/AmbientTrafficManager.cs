using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(50)] 
public class AmbientTrafficManager : MonoBehaviour
{
    [Header("Spawning Configuration")]
    public AmbientVehicle vehiclePrefab;
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

        for (int i = 0; i < maxVehicles; i++)
        {
            AmbientVehicle newVeh = Instantiate(vehiclePrefab, transform);
            newVeh.Initialize();
            newVeh.gameObject.SetActive(false);
            _vehiclePool.Enqueue(newVeh);
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

        // Try up to 3 times per frame to find a valid road.
        // This ensures the visual pool stays full even if the first random 
        // picks happen to be dead roads that fail the traffic check.
        for (int i = 0; i < 3; i++)
        {
            RoadSegment randomRoad = _allRoads[Random.Range(0, _allRoads.Length)];
            float distToCam = Vector3.Distance(randomRoad.transform.position, GetGroundCameraPosition());

            if (distToCam > innerSpawnRadius && distToCam < outerDespawnRadius)
            {
                // --- GRID TRAFFIC DENSITY CHECK ---
                if (GridManager.Instance != null)
                {
                    if (GridManager.Instance.WorldToGrid(randomRoad.transform.position, out int x, out int y))
                    {
                        TileData tile = GridManager.Instance.GetTileData(x, y);
                        
                        // Traffic is 0-100. We use it as a percentage chance to spawn.
                        // We add a minimum 5% chance so dead zones aren't completely devoid of life.
                        int spawnChance = Mathf.Max(5, tile.Traffic);

                        if (Random.Range(0, 100) > spawnChance)
                        {
                            continue; // Failed the density roll. Skip to the next attempt in the loop.
                        }
                    }
                }

                // If it passes the radius and the traffic density check, spawn it!
                SpawnFromPool(randomRoad);
                return; // Success, exit the loop for this frame.
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