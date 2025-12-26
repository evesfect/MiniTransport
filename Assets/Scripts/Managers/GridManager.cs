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
        
        Texture2D tex = layer.Texture;
        Color32[] pixels = tex.GetPixels32(); 
        int texW = tex.width;
        int texH = tex.height;

        // -----------------------------------------------------------------------
        // PASS 1: Calculate Total Density Weights (For Distribution Mappings Only)
        // -----------------------------------------------------------------------
        Dictionary<int, float> distributionSums = new Dictionary<int, float>();
        
        // FIX: Create a separate list of keys to iterate over, so we don't break the dictionary loop
        List<int> activeIndices = new List<int>();

        if (layer.DistributionMappings != null && layer.DistributionMappings.Count > 0)
        {
            for (int i = 0; i < layer.DistributionMappings.Count; i++)
            {
                if (layer.DistributionMappings[i].Enabled)
                {
                    distributionSums[i] = 0f;
                    activeIndices.Add(i);
                }
            }

            // Only loop pixels if we actually have distributions to calculate
            if (activeIndices.Count > 0)
            {
                for (int y = 0; y < resolutionZ; y++)
                {
                    for (int x = 0; x < resolutionX; x++)
                    {
                        Color32 c = SampleColor(x, y, texW, texH, pixels);

                        // FIX: Iterate over the LIST (activeIndices), while modifying the DICTIONARY (distributionSums)
                        for (int i = 0; i < activeIndices.Count; i++)
                        {
                            int index = activeIndices[i];
                            var mapping = layer.DistributionMappings[index];
                            distributionSums[index] += GetChannelValue(c, mapping.SourceChannel);
                        }
                    }
                }
            }
        }

        // -----------------------------------------------------------------------
        // PASS 2: Assign Values (Linear + Distribution)
        // -----------------------------------------------------------------------
        for (int y = 0; y < resolutionZ; y++)
        {
            for (int x = 0; x < resolutionX; x++)
            {
                int gridIndex = (y * resolutionX) + x;
                TileData data = _gridData[gridIndex];
                Color32 c = SampleColor(x, y, texW, texH, pixels);

                // A. Apply Linear Mappings (Min/Max)
                if (layer.LinearMappings != null)
                {
                    foreach (var map in layer.LinearMappings)
                    {
                        if (!map.Enabled) continue;

                        byte rawVal = GetChannelValue(c, map.SourceChannel);
                        float t = rawVal / 255f;
                        float val = Mathf.Lerp(map.MinValue, map.MaxValue, t);

                        ApplyLinearValue(ref data, map.TargetField, val);
                    }
                }

                // B. Apply Distribution Mappings (Share of Total)
                if (layer.DistributionMappings != null)
                {
                    for (int i = 0; i < layer.DistributionMappings.Count; i++)
                    {
                        var map = layer.DistributionMappings[i];
                        if (!map.Enabled) continue;
                        
                        // If the total weight on the map is 0, we can't distribute, so assign 0.
                        if (distributionSums.TryGetValue(i, out float totalWeight) && totalWeight > 0)
                        {
                            byte rawVal = GetChannelValue(c, map.SourceChannel);
                            float share = rawVal / totalWeight;
                            float assignedAmount = share * map.TotalAmount;
                            
                            ApplyDistributionValue(ref data, map.TargetField, assignedAmount);
                        }
                        else
                        {
                            ApplyDistributionValue(ref data, map.TargetField, 0);
                        }
                    }
                }

                // C. Normalize Ratios (Self-Correction)
                NormalizeRatios(ref data);

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

    private Color32 SampleColor(int x, int y, int w, int h, Color32[] pixels)
    {
        // Samples the center of the grid tile
        float u = (x + 0.5f) / (float)resolutionX;
        float v = (y + 0.5f) / (float)resolutionZ;
        int tx = Mathf.Clamp(Mathf.FloorToInt(u * w), 0, w - 1);
        int ty = Mathf.Clamp(Mathf.FloorToInt(v * h), 0, h - 1);
        return pixels[ty * w + tx];
    }

    private byte GetChannelValue(Color32 c, TextureChannel channel)
    {
        switch (channel)
        {
            case TextureChannel.Red: return c.r;
            case TextureChannel.Green: return c.g;
            case TextureChannel.Blue: return c.b;
            case TextureChannel.Alpha: return c.a;
            default: return 0;
        }
    }

    private void ApplyLinearValue(ref TileData data, LinearGridTarget target, float val)
    {
        switch (target)
        {
            case LinearGridTarget.Traffic:          data.Traffic = (byte)Mathf.Clamp(val, 0, 100); break;
            case LinearGridTarget.Demand:           data.Demand = (byte)Mathf.Clamp(val, 0, 100); break;
            case LinearGridTarget.ResidentialRatio: data.ResidentialRatio = (byte)Mathf.Clamp(val, 0, 100); break;
            case LinearGridTarget.CommercialRatio:  data.CommercialRatio = (byte)Mathf.Clamp(val, 0, 100); break;
            case LinearGridTarget.IndustrialRatio:  data.IndustrialRatio = (byte)Mathf.Clamp(val, 0, 100); break;
            case LinearGridTarget.EconomicClass:    data.EcoClass = (EconomicClass)Mathf.Clamp(Mathf.RoundToInt(val), 0, 2); break;
        }
    }

    private void ApplyDistributionValue(ref TileData data, DistributionGridTarget target, float val)
    {
        switch (target)
        {
            case DistributionGridTarget.Population: 
                data.Population = (ushort)Mathf.Clamp(val, 0, 65535); 
                break;
        }
    }

    private void NormalizeRatios(ref TileData data)
    {
        int total = data.ResidentialRatio + data.CommercialRatio + data.IndustrialRatio;
        if (total > 0 && total != 100)
        {
            float scale = 100f / total;
            int r = Mathf.RoundToInt(data.ResidentialRatio * scale);
            int c = Mathf.RoundToInt(data.CommercialRatio * scale);
            int i = 100 - r - c;
            
            // Safety adjustment to ensure sum is exactly 100
            if (i < 0) 
            { 
                i = 0; 
                if (r > c) r += (100 - r - c); 
                else c += (100 - r - c); 
            }

            data.ResidentialRatio = (byte)r;
            data.CommercialRatio = (byte)c;
            data.IndustrialRatio = (byte)i;
        }
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