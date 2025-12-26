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

    [Header("Simulation Timing")]
    [Tooltip("How often (in Game Minutes) the simulation calculates changes.")]
    public float simulationStepMinutes = 15f; 

    [Tooltip("How far in the future (Game Hours) to schedule the update on clients.")]
    public float scheduleLookaheadHours = 0.5f; 

    [Header("Debug")]
    public bool showGizmos = true;
    public bool showValues = false;
    public Color gridColor = new Color(0, 1, 0, 0.3f);
    public float gizmoHeight = 0.5f;

    // Data State
    private TileData[] _gridData;
    private Vector2 _cellSize;
    private Vector3 _gridOrigin;
    
    // Update Buffer
    private List<PendingGridUpdate> _pendingUpdates = new List<PendingGridUpdate>();
    private Dictionary<int, List<BusStop>> _tileStops = new Dictionary<int, List<BusStop>>();

    // Server State
    private float _lastSimGameTime;
    private struct ScheduledServerUpdate
    {
        public int TileIndex;
        public TileData Data;
        public TileUpdateFlags Mask;
        public float TargetTimeOfDay;
    }
    private List<ScheduledServerUpdate> _serverScheduledUpdates = new List<ScheduledServerUpdate>();
    private float _prevFrameTime;

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
        
        // Ensure data array is allocated before we try to fill it
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
        
        // Initial Defaults (Fallback)
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
            _lastSimGameTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
            _prevFrameTime = _lastSimGameTime;

            // Load default preset if configured
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
        if (IsServer) 
        {
            ProcessServerScheduledUpdates();
            ServerSimulationLoop();
        }
        
        if (IsClient || IsServer) ClientUpdateLoop();
    }

    #region Preset Loading & Texture Logic

    /// <summary>
    /// SERVER ONLY: Loads a preset by index and replicates to all clients.
    /// </summary>
    public void LoadPreset(int presetIndex)
    {
        if (!IsServer) return;
        if (presetIndex < 0 || presetIndex >= _availablePresets.Count)
        {
            Debug.LogError($"Grid: Invalid Preset Index {presetIndex}");
            return;
        }

        Debug.Log($"Grid: Server loading preset {presetIndex}...");
        
        // 1. Load locally
        ApplyPresetLocal(presetIndex);

        // 2. Tell clients to load the same preset
        LoadPresetClientRpc(presetIndex);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void LoadPresetClientRpc(int presetIndex)
    {
        // Host has already loaded it in the direct call, check to avoid double load
        if (IsServer && !IsHost) return; 
        if (IsHost && IsServer) return;  
        
        if (!IsServer) // Clients only
        {
            ApplyPresetLocal(presetIndex);
        }
    }

    private void ApplyPresetLocal(int presetIndex)
    {
        if (presetIndex < 0 || presetIndex >= _availablePresets.Count) return;
        
        GridMapPreset preset = _availablePresets[presetIndex];
        if (preset == null) return;

        foreach (var layer in preset.Layers)
        {
            ApplyTextureLayer(layer);
        }
        
        Debug.Log($"Grid: Loaded Preset '{preset.name}'");
    }

    private void ApplyTextureLayer(GridTextureLayer layer)
    {
        if (layer.Texture == null) return;
        if (layer.Mappings == null || layer.Mappings.Count == 0) return;

        Texture2D tex = layer.Texture;
        Color32[] pixels = tex.GetPixels32(); 
        int texW = tex.width;
        int texH = tex.height;

        // Iterate through all grid tiles
        for (int y = 0; y < resolutionZ; y++)
        {
            for (int x = 0; x < resolutionX; x++)
            {
                int gridIndex = GetIndex(x, y);
                TileData data = _gridData[gridIndex];

                // --- SAMPLING LOGIC ---
                float u = (x + 0.5f) / (float)resolutionX;
                float v = (y + 0.5f) / (float)resolutionZ;

                int tx = Mathf.FloorToInt(u * texW);
                int ty = Mathf.FloorToInt(v * texH);
                tx = Mathf.Clamp(tx, 0, texW - 1);
                ty = Mathf.Clamp(ty, 0, texH - 1);

                int pixelIndex = ty * texW + tx;
                Color32 color = pixels[pixelIndex];

                // --- APPLY MAPPINGS ---
                foreach (var mapping in layer.Mappings)
                {
                    if (!mapping.Enabled) continue;

                    byte channelValue = 0;
                    switch (mapping.SourceChannel)
                    {
                        case TextureChannel.Red: channelValue = color.r; break;
                        case TextureChannel.Green: channelValue = color.g; break;
                        case TextureChannel.Blue: channelValue = color.b; break;
                        case TextureChannel.Alpha: channelValue = color.a; break;
                    }

                    float t = channelValue / 255f;
                    float finalValue = Mathf.Lerp(mapping.MinValue, mapping.MaxValue, t);

                    switch (mapping.TargetField)
                    {
                        case GridTargetField.Traffic:
                            data.Traffic = (byte)Mathf.Clamp(finalValue, 0, 100);
                            break;
                        case GridTargetField.Population:
                            data.Population = (ushort)Mathf.Clamp(finalValue, 0, 65535);
                            break;
                        case GridTargetField.Demand:
                            data.Demand = (byte)Mathf.Clamp(finalValue, 0, 100);
                            break;
                        case GridTargetField.ResidentialRatio:
                            data.ResidentialRatio = (byte)Mathf.Clamp(finalValue, 0, 100);
                            break;
                        case GridTargetField.CommercialRatio:
                            data.CommercialRatio = (byte)Mathf.Clamp(finalValue, 0, 100);
                            break;
                        case GridTargetField.IndustrialRatio:
                            data.IndustrialRatio = (byte)Mathf.Clamp(finalValue, 0, 100);
                            break;
                        case GridTargetField.EconomicClass:
                            int enumVal = Mathf.RoundToInt(finalValue);
                            data.EcoClass = (EconomicClass)Mathf.Clamp(enumVal, 0, 2);
                            break;
                    }
                }

                // --- RATIO NORMALIZATION ---
                // Automatically ensures R + C + I = 100
                int totalRatio = data.ResidentialRatio + data.CommercialRatio + data.IndustrialRatio;
                if (totalRatio > 0)
                {
                    float scale = 100f / totalRatio;

                    // Round to nearest to minimize error
                    int r = Mathf.RoundToInt(data.ResidentialRatio * scale);
                    int c = Mathf.RoundToInt(data.CommercialRatio * scale);
                    
                    // Assign remainder to Industrial to ensure perfect sum of 100
                    // (Handling edge case where r+c > 100 due to rounding)
                    if (r + c > 100) 
                    {
                        if (r > c) r = 100 - c; else c = 100 - r;
                    }

                    data.ResidentialRatio = (byte)r;
                    data.CommercialRatio = (byte)c;
                    data.IndustrialRatio = (byte)(100 - r - c);
                }

                _gridData[gridIndex] = data;
            }
        }
    }
    #endregion

    #region Server Logic

    private void ServerSimulationLoop()
    {
        if (SimulationTimeManager.Instance == null) return;

        float currentGameTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        
        float timeDelta = currentGameTime - _lastSimGameTime;
        if (timeDelta < 0) timeDelta += 24f; // Wrapped around midnight

        float minutesPassed = timeDelta * 60f;

        if (minutesPassed >= simulationStepMinutes)
        {
            _lastSimGameTime = currentGameTime;
            PerformSimulationStep();
        }
    }

    private void PerformSimulationStep()
    {
        // Future simulation logic will go here.
        // It should call ScheduleTileUpdate(...) when it decides to change a tile.
    }

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
    }

    public void ScheduleTileUpdateAtGameTime(int tileIndex, TileData newData, TileUpdateFlags mask, float targetTimeOfDay)
    {
        if (!IsServer) return;

        _serverScheduledUpdates.Add(new ScheduledServerUpdate
        {
            TileIndex = tileIndex,
            Data = newData,
            Mask = mask,
            TargetTimeOfDay = targetTimeOfDay
        });
    }

    private void ProcessServerScheduledUpdates()
    {
        if (SimulationTimeManager.Instance == null) return;
        
        float currentTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        float lookahead = scheduleLookaheadHours;

        for (int i = _serverScheduledUpdates.Count - 1; i >= 0; i--)
        {
            ScheduledServerUpdate update = _serverScheduledUpdates[i];
            
            float triggerTime = update.TargetTimeOfDay - lookahead;
            if (triggerTime < 0) triggerTime += 24f;

            if (WasTimeCrossed(_prevFrameTime, currentTime, triggerTime))
            {
                ScheduleTileUpdate(update.TileIndex, update.Data, update.Mask);
                _serverScheduledUpdates.RemoveAt(i);
            }
        }

        _prevFrameTime = currentTime;
    }

    private bool WasTimeCrossed(float prev, float curr, float target)
    {
        if (prev < curr) return prev < target && target <= curr;
        else return target > prev || target <= curr;
    }

    [Rpc(SendTo.Server)]
    private void RequestGridStateServerRpc(RpcParams rpcParams = default)
    {
        SendFullStateClientRpc(_gridData, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
    }
    #endregion

    #region Client and Host Logic

    [Rpc(SendTo.SpecifiedInParams)]
    private void SendFullStateClientRpc(TileData[] fullState, RpcParams rpcParams = default)
    {
        if (fullState.Length != _gridData.Length)
        {
            Debug.LogError($"Grid Size Mismatch! Server: {fullState.Length}, Client: {_gridData.Length}");
            // Optional: Could trigger a texture reload here if we tracked current preset index
            return;
        }

        _gridData = fullState;
        Debug.Log("Grid [Client]: Received Initial Full State from Server.");
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

    #region Bus Stops and Helpers

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

                if (_gridData[i].Traffic > 0)
                {
                    Gizmos.color = new Color(1f, 0f, 0f, 0.4f); 
                    float h = (_gridData[i].Traffic / 100f) * 20f; 
                    Gizmos.DrawCube(pos + Vector3.up * (h/2), new Vector3(_cellSize.x * 0.8f, h, _cellSize.y * 0.8f));
                    Gizmos.DrawWireCube(pos + Vector3.up * (h/2), new Vector3(_cellSize.x * 0.8f, h, _cellSize.y * 0.8f));
                }
            }
        }
    }
#endif
    #endregion
}