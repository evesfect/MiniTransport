using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct AmbientVehicleSpawnDef
{
    public AmbientVehicle vehiclePrefab;
    [Range(1, 100)] public int spawnWeight;
}

[DefaultExecutionOrder(50)]
public class AmbientTrafficManager : MonoBehaviour
{
    [Header("Spawning Configuration")]
    public List<AmbientVehicleSpawnDef> vehicleTypes = new List<AmbientVehicleSpawnDef>();
    public int maxVehicles = 1000;
    
    [Header("Optimization Timers")]
    public float balanceInterval = 0.25f; 
    public float trafficUpdateInterval = 2.0f;
    public float cameraMoveThreshold = 1.0f;
    public float cameraAngleThreshold = 2.0f;

    [Header("Sector Wake-up Settings")]
    public float frustumBuffer = 40f; 

    [Header("Demand Tuning (Density Based)")]
    public int minCarsPerActiveTile = 1; 
    [Tooltip("How many cars should spawn per 100 units of road when Traffic is at 100%")]
    public float maxCarsPer100Units = 10f; 

    [Header("Debug & Gizmos")]
    public bool showGizmos = true;
    public Color activeTileColor = new Color(0, 1, 0, 0.2f);
    public Color bufferedTileColor = new Color(1, 1, 0, 0.2f);
    public float gizmoTextHeight = 2.0f;

    // Core Data
    private Dictionary<int, List<RoadSegment>> _tileRoads = new Dictionary<int, List<RoadSegment>>();
    private Dictionary<int, float> _tileRoadLengths = new Dictionary<int, float>(); // THE NEW CACHE
    
    private Queue<AmbientVehicle> _vehiclePool = new Queue<AmbientVehicle>();
    private List<AmbientVehicle> _activeVehicles = new List<AmbientVehicle>();

    // Decoupled State
    private HashSet<int> _activeSectors = new HashSet<int>();
    private Dictionary<int, int> _sectorTargetQuotas = new Dictionary<int, int>();
    private float _lastScaleFactor = 1.0f; 
    private Plane[] _cameraFrustum;

    // Camera Tracking
    private Vector3 _lastCamPos;
    private Quaternion _lastCamRot;

    // Timers
    private float _balanceTimer = 0f;
    private float _trafficTimer = 0f;

    private void Start()
    {
        InitializeWeightedPool();
        Invoke(nameof(MapRoadsToSectors), 0.1f); 
    }

    private void InitializeWeightedPool()
    {
        if (vehicleTypes == null || vehicleTypes.Count == 0) return;

        List<AmbientVehicle> initialSpawnList = new List<AmbientVehicle>();
        int totalWeight = 0;
        foreach (var def in vehicleTypes) totalWeight += def.spawnWeight;

        int spawnedCount = 0;

        for (int i = 0; i < vehicleTypes.Count; i++)
        {
            var def = vehicleTypes[i];
            if (def.vehiclePrefab == null) continue;

            int amountToSpawn = Mathf.RoundToInt(((float)def.spawnWeight / totalWeight) * maxVehicles);
            if (i == vehicleTypes.Count - 1) amountToSpawn = maxVehicles - spawnedCount;

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

        ShuffleList(initialSpawnList);
        foreach (var veh in initialSpawnList) _vehiclePool.Enqueue(veh);
    }

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

    private void MapRoadsToSectors()
    {
        if (GridManager.Instance == null) return;

        RoadSegment[] allRoads = FindObjectsByType<RoadSegment>(FindObjectsSortMode.None);
        foreach (var road in allRoads)
        {
            Vector3 roadPos = road.NodeA != null ? road.NodeA.transform.position : road.transform.position;
            if (GridManager.Instance.WorldToGrid(roadPos, out int x, out int y))
            {
                int index = GridManager.Instance.GetIndex(x, y);
                
                if (!_tileRoads.ContainsKey(index)) 
                {
                    _tileRoads[index] = new List<RoadSegment>();
                    _tileRoadLengths[index] = 0f; // Initialize the length float
                }
                
                _tileRoads[index].Add(road);
                _tileRoadLengths[index] += road.Length; // Add the physical asphalt length
            }
        }
    }

    private void Update()
    {
        if (Camera.main == null || GridManager.Instance == null) return;

        _cameraFrustum = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        UpdateVehicleMovement();

        bool cameraMoved = (Camera.main.transform.position - _lastCamPos).sqrMagnitude > (cameraMoveThreshold * cameraMoveThreshold);
        bool cameraRotated = Quaternion.Angle(Camera.main.transform.rotation, _lastCamRot) > cameraAngleThreshold;

        if (cameraMoved || cameraRotated)
        {
            _lastCamPos = Camera.main.transform.position;
            _lastCamRot = Camera.main.transform.rotation;
            CheckForNewSectors(); 
        }

        _balanceTimer += Time.deltaTime;
        if (_balanceTimer >= balanceInterval)
        {
            _balanceTimer = 0f;
            BalanceAllActiveSectors();
        }

        _trafficTimer += Time.deltaTime;
        if (_trafficTimer >= trafficUpdateInterval)
        {
            _trafficTimer = 0f;
            RecalculateTrafficMath();
        }
    }

    // --- THE NEW DENSITY MATH HELPER ---
    private int CalculateIdealDemandForSector(int sector)
    {
        TileData tile = GridManager.Instance.GetTileData(sector);
        
        // Safety check in case a tile has no roads cached
        float roadLength = _tileRoadLengths.ContainsKey(sector) ? _tileRoadLengths[sector] : 0f;
        
        // Assuming tile.Traffic is a 0-100 value.
        float trafficPercentage = tile.Traffic / 100f; 
        
        // Example: (300 units / 100) * 0.8 traffic * 10 max cars = 24 cars
        int lengthBasedDemand = Mathf.RoundToInt((roadLength / 100f) * trafficPercentage * maxCarsPer100Units);
        
        return minCarsPerActiveTile + lengthBasedDemand;
    }

    private void CheckForNewSectors()
    {
        HashSet<int> currentlyVisibleSectors = new HashSet<int>();

        foreach (int sectorIndex in _tileRoads.Keys)
        {
            GridManager.Instance.GetXY(sectorIndex, out int x, out int y);
            Vector3 tileWorldPos = GridManager.Instance.GridToWorld(x, y);

            if (IsPointInBufferedFrustum(tileWorldPos))
            {
                currentlyVisibleSectors.Add(sectorIndex);
            }
        }

        foreach (int sector in currentlyVisibleSectors)
        {
            if (!_activeSectors.Contains(sector))
            {
                _activeSectors.Add(sector);
                
                int idealCars = CalculateIdealDemandForSector(sector);
                _sectorTargetQuotas[sector] = Mathf.FloorToInt(idealCars * _lastScaleFactor);
                
                BalanceSingleSector(sector); 
            }
        }

        List<int> sectorsToRemove = new List<int>();
        foreach (int sector in _activeSectors)
        {
            if (!currentlyVisibleSectors.Contains(sector))
            {
                sectorsToRemove.Add(sector);
            }
        }

        foreach (int sector in sectorsToRemove)
        {
            _activeSectors.Remove(sector);
            _sectorTargetQuotas.Remove(sector);
        }

        foreach (var veh in _activeVehicles)
        {
            if (veh.IsDespawning) continue;
            if (GridManager.Instance.WorldToGrid(veh.transform.position, out int x, out int y))
            {
                if (!_activeSectors.Contains(GridManager.Instance.GetIndex(x, y)))
                {
                    veh.TriggerDespawnFade();
                }
            }
        }
    }

    private void RecalculateTrafficMath()
    {
        if (_activeSectors.Count == 0) return;

        int totalIdealDemand = 0;
        Dictionary<int, int> idealDemands = new Dictionary<int, int>();

        foreach (int sector in _activeSectors)
        {
            int idealCars = CalculateIdealDemandForSector(sector);
            idealDemands[sector] = idealCars;
            totalIdealDemand += idealCars;
        }

        _lastScaleFactor = 1.0f;
        if (totalIdealDemand > maxVehicles)
        {
            _lastScaleFactor = (float)maxVehicles / totalIdealDemand;
        }

        foreach (int sector in _activeSectors)
        {
            _sectorTargetQuotas[sector] = Mathf.FloorToInt(idealDemands[sector] * _lastScaleFactor);
        }
    }

    private void BalanceAllActiveSectors()
    {
        if (_activeSectors.Count == 0) return;

        Dictionary<int, int> currentCounts = new Dictionary<int, int>();
        foreach (int s in _activeSectors) currentCounts[s] = 0;

        foreach (var veh in _activeVehicles)
        {
            if (veh.IsDespawning) continue;
            if (GridManager.Instance.WorldToGrid(veh.transform.position, out int x, out int y))
            {
                int index = GridManager.Instance.GetIndex(x, y);
                if (currentCounts.ContainsKey(index)) currentCounts[index]++;
            }
        }

        foreach (int sector in _activeSectors)
        {
            int target = _sectorTargetQuotas.ContainsKey(sector) ? _sectorTargetQuotas[sector] : 0;
            int current = currentCounts[sector];

            ProcessSectorSpawnKill(sector, current, target);
        }
    }

    private void BalanceSingleSector(int sector)
    {
        int target = _sectorTargetQuotas[sector];
        int current = 0;

        foreach (var veh in _activeVehicles)
        {
            if (veh.IsDespawning) continue;
            if (GridManager.Instance.WorldToGrid(veh.transform.position, out int x, out int y))
            {
                if (GridManager.Instance.GetIndex(x, y) == sector) current++;
            }
        }

        ProcessSectorSpawnKill(sector, current, target);
    }

    private void ProcessSectorSpawnKill(int sector, int current, int target)
    {
        if (current < target)
        {
            int amountToSpawn = target - current;
            if (!_tileRoads.ContainsKey(sector)) return;
            List<RoadSegment> localRoads = _tileRoads[sector];
            
            for (int i = 0; i < amountToSpawn; i++)
            {
                if (_vehiclePool.Count == 0 || localRoads.Count == 0) break;
                RoadSegment randomRoad = localRoads[Random.Range(0, localRoads.Count)];
                SpawnFromPool(randomRoad);
            }
        }
        else if (current > target)
        {
            int amountToKill = current - target;
            foreach (var veh in _activeVehicles)
            {
                if (amountToKill <= 0) break;
                if (veh.IsDespawning) continue;

                if (GridManager.Instance.WorldToGrid(veh.transform.position, out int x, out int y))
                {
                    if (GridManager.Instance.GetIndex(x, y) == sector)
                    {
                        veh.TriggerDespawnFade();
                        amountToKill--;
                    }
                }
            }
        }
    }

    private bool IsPointInBufferedFrustum(Vector3 point)
    {
        if (_cameraFrustum == null) return true;
        for (int i = 0; i < 6; i++)
        {
            if (_cameraFrustum[i].GetDistanceToPoint(point) < -frustumBuffer) return false;
        }
        return true;
    }

    private bool IsPointInStrictFrustum(Vector3 point)
    {
        if (_cameraFrustum == null) return true;
        for (int i = 0; i < 6; i++)
        {
            if (_cameraFrustum[i].GetDistanceToPoint(point) < 0f) return false;
        }
        return true;
    }

    private void UpdateVehicleMovement()
    {
        float dt = Time.deltaTime;
        for (int i = _activeVehicles.Count - 1; i >= 0; i--)
        {
            AmbientVehicle veh = _activeVehicles[i];
            bool isVisible = IsPointInBufferedFrustum(veh.transform.position);
            bool actionRequired = veh.CustomUpdate(dt, isVisible);

            if (actionRequired)
            {
                if (veh.IsDespawning) ReturnToPool(veh, i);
                else PickNextNode(veh);
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
        veh.DistanceTraveledOnSegment = Random.Range(0f, startSegment.Length);
        
        float evalT = veh.DistanceTraveledOnSegment / startSegment.Length;
        if (!veh.IsHeadingToNodeB) evalT = 1f - evalT;

        veh.transform.position = startSegment.GetPointOnRoad(evalT, veh.IsHeadingToNodeB);
        veh.IsActive = true;
        _activeVehicles.Add(veh);
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

    private void OnGUI()
    {
        if (!showGizmos) return;

        int totalAlive = 0;
        foreach (var veh in _activeVehicles)
        {
            if (!veh.IsDespawning) totalAlive++;
        }

        GUIStyle style = new GUIStyle();
        style.fontSize = 24;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.cyan;
        GUI.Label(new Rect(20, 20, 500, 40), $"TOTAL ALIVE CARS: {totalAlive} / {maxVehicles}", style);
        
        style.fontSize = 18;
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(20, 50, 500, 40), $"Active Sectors Awake: {_activeSectors.Count}", style);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showGizmos || GridManager.Instance == null || _cameraFrustum == null) return;

        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
        style.fontSize = 12;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;

        Vector3 size = new Vector3(GridManager.Instance.CellSize.x, 0.1f, GridManager.Instance.CellSize.y);

        Dictionary<int, int> debugCounts = new Dictionary<int, int>();
        foreach (var veh in _activeVehicles)
        {
            if (veh.IsDespawning) continue;
            if (GridManager.Instance.WorldToGrid(veh.transform.position, out int x, out int y))
            {
                int idx = GridManager.Instance.GetIndex(x, y);
                if (!debugCounts.ContainsKey(idx)) debugCounts[idx] = 0;
                debugCounts[idx]++;
            }
        }

        foreach (var kvp in _sectorTargetQuotas)
        {
            int sector = kvp.Key;
            int target = kvp.Value;
            int current = debugCounts.ContainsKey(sector) ? debugCounts[sector] : 0;

            GridManager.Instance.GetXY(sector, out int x, out int y);
            Vector3 center = GridManager.Instance.GridToWorld(x, y);

            bool isStrictlyInside = IsPointInStrictFrustum(center);
            Gizmos.color = isStrictlyInside ? activeTileColor : bufferedTileColor;
            Gizmos.DrawCube(center, size);
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 1f); 
            Gizmos.DrawWireCube(center, size);

            string label = $"Cars: {current} / {target}\nL: {_tileRoadLengths[sector]:F0}m";
            UnityEditor.Handles.Label(center + Vector3.up * gizmoTextHeight, label, style);
        }
    }
#endif
}