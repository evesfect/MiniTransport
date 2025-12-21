using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-50)]
public class GridManager : NetworkBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Configuration")]
    [SerializeField] private Terrain _targetTerrain;

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
    
    // Update Buffer (Used by Client AND Host)
    private List<PendingGridUpdate> _pendingUpdates = new List<PendingGridUpdate>();

    // Server State
    private float _lastSimGameTime;

    // Public Accessors
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
        InitializeGrid();
    }

    private void InitializeGrid()
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
            _lastSimGameTime = SimulationTimeManager.Instance.CurrentTimeOfDay;
        }
        else
        {
            RequestGridStateServerRpc();
        }
    }

    private void Update()
    {
        if (IsServer) ServerSimulationLoop();
        
        // IMPORTANT: ClientUpdateLoop runs on both Client AND Host/Server
        // This ensures the Host waits for the simulation clock just like clients.
        if (IsClient || IsServer) ClientUpdateLoop();
    }

    // ===========================================================================================
    // SERVER LOGIC
    // ===========================================================================================

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

        // FIXED: Do NOT apply to _gridData immediately. 
        // We let the RPC/Buffer handle it so the Host waits for the correct time.

        // 1. Calculate Schedule Time
        float executionTime = SimulationTimeManager.Instance.CurrentTimeOfDay + scheduleLookaheadHours;
        if (executionTime >= 24f) executionTime -= 24f;

        // 2. Prepare Packet
        TileUpdatePacket packet = new TileUpdatePacket
        {
            TileIndex = tileIndex,
            Data = newData,
            Mask = mask
        };

        // 3. Send Network Packet (Sends to Clients AND Host)
        ScheduleGridUpdateClientRpc(executionTime, packet);
    }

    [Rpc(SendTo.Server)]
    private void RequestGridStateServerRpc(RpcParams rpcParams = default)
    {
        SendFullStateClientRpc(_gridData, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
    }

    // ===========================================================================================
    // CLIENT (AND HOST) LOGIC
    // ===========================================================================================

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

    [Rpc(SendTo.ClientsAndHost)]
    private void ScheduleGridUpdateClientRpc(float executionGameTime, TileUpdatePacket packet)
    {
        // Both Clients and Host execute this.
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

        // On Host, VisualTime == NetTimeOfDay. On Client, it is Interpolated.
        float visualTime = SimulationTimeManager.Instance.VisualTime;
        
        while(_pendingUpdates.Count > 0)
        {
            var nextUpdate = _pendingUpdates[0];

            bool isTime = visualTime >= nextUpdate.ExecutionTime;
            
            // Handle day wrap (Packet is for 23.9, we are at 0.1 -> we missed it, apply now)
            if (visualTime < 1f && nextUpdate.ExecutionTime > 23f) isTime = true;
            
            // Prevent premature firing on day wrap (Packet is for 0.1, we are at 23.9 -> wait)
            if (visualTime > 23f && nextUpdate.ExecutionTime < 1f) isTime = false;

            if (isTime) 
            {
                 ApplyUpdate(nextUpdate.Packet);
                _pendingUpdates.RemoveAt(0);
            }
            else
            {
                break;
            }
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

    // ===========================================================================================
    // HELPERS & GIZMOS
    // ===========================================================================================

    public TileData GetTileData(int x, int y)
    {
        if (x < 0 || x >= resolutionX || y < 0 || y >= resolutionZ) return default;
        return _gridData[GetIndex(x, y)];
    }

    public int GetIndex(int x, int y) => (y * resolutionX) + x;
    
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

    public void SetTileData(int index, TileData newData)
    {
        if (index >= 0 && index < _gridData.Length)
        {
            _gridData[index] = newData;
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
                Handles.Label(pos + Vector3.up * (gizmoHeight + 2f), label, style);

                if (_gridData[i].Traffic > 0)
                {
                    Gizmos.color = new Color(1f, 0f, 0f, 0.4f); 
                    float h = (_gridData[i].Traffic / 100f) * 20f; 
                    Gizmos.DrawCube(pos + Vector3.up * (h/2), new Vector3(_cellSize.x * 0.8f, h, _cellSize.y * 0.8f));
                    
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireCube(pos + Vector3.up * (h/2), new Vector3(_cellSize.x * 0.8f, h, _cellSize.y * 0.8f));
                }
            }
        }
    }
#endif
}