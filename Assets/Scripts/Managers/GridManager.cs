using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[DefaultExecutionOrder(-50)]
public class GridManager : NetworkBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Configuration")]
    [SerializeField] private Terrain _targetTerrain;

    [Header("Map Presets")]
    [SerializeField] private List<GridMapPreset> _availablePresets = new List<GridMapPreset>();
    [Tooltip("If true, the first preset in the list is loaded on start.")]
    [SerializeField] private bool _loadDefaultPresetOnStart = false;

    [Header("Resolution")]
    [Min(1)] public int resolutionX = 20;
    [Min(1)] public int resolutionZ = 20;

    [Header("Network Config")]
    [Tooltip("How far in the future (Game Hours) to schedule the update on clients.")]
    public float scheduleLookaheadHours = 0.5f; 

    [Header("Debug")]
    public bool showGizmos = true;
    public bool showValues = false;
    public Color gridColor = new Color(0, 1, 0, 0.3f);
    public float gizmoHeight = 0.5f;

    private TileData[] _gridData;
    private Vector2 _cellSize;
    private Vector3 _gridOrigin;
    
    private List<PendingGridUpdate> _pendingUpdates = new List<PendingGridUpdate>();
    private Dictionary<int, List<BusStop>> _tileStops = new Dictionary<int, List<BusStop>>();

    public int TotalTiles => resolutionX * resolutionZ;
    public Vector3 Origin => _gridOrigin;
    public Vector2 CellSize => _cellSize;
    public int Width => resolutionX;
    public int Height => resolutionZ;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitializeGridStructure();
    }

    private void InitializeGridStructure()
    {
        if (_targetTerrain == null) _targetTerrain = Terrain.activeTerrain;
        if (_targetTerrain == null)
        {
            _gridOrigin = Vector3.zero;
            _cellSize = new Vector2(5f, 5f);
        }
        else
        {
            Vector3 terrainSize = _targetTerrain.terrainData.size;
            _gridOrigin = _targetTerrain.transform.position;
            float sizeX = terrainSize.x / resolutionX;
            float sizeZ = terrainSize.z / resolutionZ;
            _cellSize = new Vector2(sizeX, sizeZ);
        }

        _gridData = new TileData[resolutionX * resolutionZ];
        
        // Initial Defaults
        for(int i=0; i<_gridData.Length; i++)
        {
            _gridData[i] = new TileData
            {
                Traffic = 0,
                Population = 50,
                Demand = 20,
                ResidentialRatio = 100,
                EcoClass = EconomicClass.Medium
            };
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Debug.Log($"Grid: Initialized as Server/Host. Total Tiles: {TotalTiles}");
            
            if (_loadDefaultPresetOnStart && _availablePresets.Count > 0)
            {
                LoadPreset(0);
            }
        }
        else
        {
            RequestGridStateServerRpc();
        }
    }

    private void Update()
    {
        // Both Client and Server (Host/Dedicated) process the update buffer
        // to ensure the simulation state changes at the exact same 'Visual Time'.
        if (IsClient || IsServer) 
        {
            ClientUpdateLoop();
        }
    }

    #region Preset Loading

    public void LoadPreset(int presetIndex)
    {
        if (!IsServer) return;
        if (presetIndex < 0 || presetIndex >= _availablePresets.Count)
        {
            Debug.LogError($"Grid: Invalid Preset Index {presetIndex}");
            return;
        }

        Debug.Log($"Grid: Server loading preset {presetIndex}...");
        
        ApplyPresetLocal(presetIndex);
        LoadPresetClientRpc(presetIndex);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void LoadPresetClientRpc(int presetIndex)
    {
        if (IsServer && !IsHost) return; 
        if (IsHost && IsServer) return;  
        
        if (!IsServer)
        {
            ApplyPresetLocal(presetIndex);
        }
    }

    private void ApplyPresetLocal(int presetIndex)
    {
        var preset = _availablePresets[presetIndex];
        GridInitializer.ApplyPreset(_gridData, preset, resolutionX, resolutionZ);
        Debug.Log($"Grid: Loaded Preset '{preset.name}'");
    }

    #endregion

    #region Data Access & Updates

    public void ScheduleTileUpdate(int tileIndex, TileData newData, TileUpdateFlags mask)
    {
        if (!IsServer) return;

        float executionTime = SimulationTimeManager.Instance.CurrentTimeOfDay + scheduleLookaheadHours;
        if (executionTime >= 24f) executionTime -= 24f;

        TileUpdatePacket packet = new TileUpdatePacket
        {
            TileIndex = tileIndex,
            Data = newData,
            Mask = mask
        };

        ScheduleGridUpdateClientRpc(executionTime, packet);

        // If this is a Dedicated Server (not a Host), the RPC won't loop back
        // must schedule it manually so the server state stays in sync with clients.
        if (!IsHost)
        {
            _pendingUpdates.Add(new PendingGridUpdate
            {
                ExecutionTime = executionTime,
                Packet = packet
            });
            _pendingUpdates.Sort((a, b) => a.ExecutionTime.CompareTo(b.ExecutionTime));
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ScheduleGridUpdateClientRpc(float executionGameTime, TileUpdatePacket packet)
    {
        _pendingUpdates.Add(new PendingGridUpdate
        {
            ExecutionTime = executionGameTime,
            Packet = packet
        });

        _pendingUpdates.Sort((a, b) => a.ExecutionTime.CompareTo(b.ExecutionTime));
    }

    private void ClientUpdateLoop()
    {
        if (_pendingUpdates.Count == 0) return;

        float visualTime = SimulationTimeManager.Instance.VisualTime;
        
        while(_pendingUpdates.Count > 0)
        {
            var nextUpdate = _pendingUpdates[0];
            bool isTime = visualTime >= nextUpdate.ExecutionTime;
            
            if (visualTime < 1f && nextUpdate.ExecutionTime > 23f) isTime = true;
            if (visualTime > 23f && nextUpdate.ExecutionTime < 1f) isTime = false;

            if (isTime) 
            {
                 ApplyUpdate(nextUpdate.Packet);
                _pendingUpdates.RemoveAt(0);
            }
            else break;
        }
    }

    private void ApplyUpdate(TileUpdatePacket packet)
    {
        if (packet.TileIndex < 0 || packet.TileIndex >= _gridData.Length) return;

        TileData current = _gridData[packet.TileIndex];

        if ((packet.Mask & TileUpdateFlags.Traffic) != 0) current.Traffic = packet.Data.Traffic;
        if ((packet.Mask & TileUpdateFlags.Population) != 0) current.Population = packet.Data.Population;
        if ((packet.Mask & TileUpdateFlags.Demand) != 0) current.Demand = packet.Data.Demand;
        if ((packet.Mask & TileUpdateFlags.Ratios) != 0) 
        {
            current.ResidentialRatio = packet.Data.ResidentialRatio;
            current.CommercialRatio = packet.Data.CommercialRatio;
            current.IndustrialRatio = packet.Data.IndustrialRatio;
        }
        if ((packet.Mask & TileUpdateFlags.Economy) != 0) current.EcoClass = packet.Data.EcoClass;

        _gridData[packet.TileIndex] = current;
    }

    #endregion

    #region Network Sync

    [Rpc(SendTo.Server)]
    private void RequestGridStateServerRpc(RpcParams rpcParams = default)
    {
        SendFullStateClientRpc(_gridData, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void SendFullStateClientRpc(TileData[] fullState, RpcParams rpcParams = default)
    {
        if (fullState.Length != _gridData.Length)
        {
            Debug.LogError($"Grid Size Mismatch! Server: {fullState.Length}, Client: {_gridData.Length}");
            return;
        }

        _gridData = fullState;
        Debug.Log("Grid [Client]: Received Initial Full State from Server.");
    }

    #endregion

    #region Helpers & Gizmos

    public void RegisterStop(BusStop stop)
    {
        if (stop == null) return;
        if (WorldToGrid(stop.transform.position, out int x, out int y))
        {
            int index = GetIndex(x, y);
            if (!_tileStops.ContainsKey(index)) _tileStops[index] = new List<BusStop>();
            if (!_tileStops[index].Contains(stop)) _tileStops[index].Add(stop);
        }
    }

    public List<BusStop> GetStopsInTile(int index)
    {
        if (_tileStops.TryGetValue(index, out List<BusStop> stops)) return stops;
        return null;
    }

    public TileData GetTileData(int x, int y)
    {
        if (x < 0 || x >= resolutionX || y < 0 || y >= resolutionZ) return default;
        return _gridData[GetIndex(x, y)];
    }

    public TileData GetTileData(int index) => _gridData[index];

    public int GetIndex(int x, int y) => (y * resolutionX) + x;

    public void GetXY(int index, out int x, out int y)
    {
        x = index % resolutionX;
        y = index / resolutionX;
    }
    
    public bool WorldToGrid(Vector3 worldPos, out int x, out int y)
    {
        float relX = worldPos.x - _gridOrigin.x;
        float relZ = worldPos.z - _gridOrigin.z;
        x = Mathf.FloorToInt(relX / _cellSize.x);
        y = Mathf.FloorToInt(relZ / _cellSize.y);
        return (x >= 0 && x < resolutionX && y >= 0 && y < resolutionZ);
    }
    
    public Vector3 GridToWorld(int x, int y)
    {
         float posX = _gridOrigin.x + (x * _cellSize.x) + (_cellSize.x * 0.5f);
         float posZ = _gridOrigin.z + (y * _cellSize.y) + (_cellSize.y * 0.5f);
         float height = _gridOrigin.y;
         if (_targetTerrain != null) height = _targetTerrain.SampleHeight(new Vector3(posX, 0, posZ)) + _gridOrigin.y;
         return new Vector3(posX, height, posZ);
    }

    public float GetTrafficModifierAt(Vector3 worldPos)
    {
        if (WorldToGrid(worldPos, out int x, out int y))
        {
            byte traffic = _gridData[GetIndex(x, y)].Traffic;
            return Mathf.Lerp(1.0f, 0.2f, traffic / 100f);
        }
        return 1.0f;
    }
    
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showGizmos) return;
        
        Vector3 origin = _gridOrigin;
        Vector2 cSize = _cellSize;
        int rX = resolutionX;
        int rZ = resolutionZ;

        if (!Application.isPlaying)
        {
            if (_targetTerrain == null) _targetTerrain = GetComponent<Terrain>();
            if (_targetTerrain == null) return;
            
            origin = _targetTerrain.transform.position;
            Vector3 size = _targetTerrain.terrainData.size;
            cSize = new Vector2(size.x / rX, size.z / rZ);
        }

        Gizmos.color = gridColor;
        
        Vector3 bottomLeft = origin;
        Vector3 bottomRight = origin + new Vector3(cSize.x * rX, 0, 0);
        Vector3 topLeft = origin + new Vector3(0, 0, cSize.y * rZ);
        Vector3 topRight = origin + new Vector3(cSize.x * rX, 0, cSize.y * rZ);
        
        Gizmos.DrawLine(bottomLeft, bottomRight);
        Gizmos.DrawLine(bottomLeft, topLeft);
        Gizmos.DrawLine(bottomRight, topRight);
        Gizmos.DrawLine(topLeft, topRight);

        for (int i = 1; i < rX; i++)
        {
            float x = origin.x + i * cSize.x;
            Gizmos.DrawLine(new Vector3(x, origin.y, origin.z), new Vector3(x, origin.y, origin.z + cSize.y * rZ));
        }
        for (int i = 1; i < rZ; i++)
        {
            float z = origin.z + i * cSize.y;
            Gizmos.DrawLine(new Vector3(origin.x, origin.y, z), new Vector3(origin.x + cSize.x * rX, origin.y, z));
        }
        
        if (Application.isPlaying && showValues && _gridData != null)
        {
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontSize = 11;
            style.alignment = TextAnchor.MiddleCenter;

            for(int i=0; i< _gridData.Length; i++)
            {
                int x = i % resolutionX;
                int y = i / resolutionX;
                Vector3 pos = GridToWorld(x,y);
                
                string label = $"T:{_gridData[i].Traffic}\nP:{_gridData[i].Population}\nD:{_gridData[i].Demand}";
                
#if UNITY_EDITOR
                UnityEditor.Handles.Label(pos + Vector3.up * (gizmoHeight + 2f), label, style);
#endif
            }
        }
    }
#endif
    #endregion
}