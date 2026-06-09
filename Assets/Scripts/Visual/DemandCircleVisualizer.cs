using UnityEngine;
using System.Collections.Generic;

public enum DemandType
{
    InDemand,
    OutDemand
}

public class DemandCircleVisualizer : MonoBehaviour
{
    [Header("Demand Type")]
    public DemandType demandType = DemandType.InDemand;

    [Header("Threshold")]
    [Tooltip("Tiles with demand at or below this value won't show a circle")]
    [Range(0, 100)]
    public byte demandThreshold = 10;

    [Header("Circle Size")]
    [Tooltip("Circle radius at threshold+1 demand")]
    public float minCircleSize = 1f;
    [Tooltip("Circle radius at max demand (100)")]
    public float maxCircleSize = 8f;

    [Header("Circle Mesh")]
    [Tooltip("Number of segments for the circle mesh")]
    public int segments = 32;

    [Header("Material")]
    [Tooltip("Assign your own material (use an unlit shader)")]
    public Material circleMaterial;

    [Header("Height")]
    [Tooltip("Height offset above the tile's terrain position")]
    public float heightOffset = 0.2f;
    [Tooltip("Layer mask for terrain raycasts")]
    public LayerMask terrainLayerMask;

    [Header("Update Rate")]
    [Tooltip("Seconds between demand checks")]
    public float updateInterval = 1f;

    [Header("Mock Data (temp)")]
    [Tooltip("Use mock data instead of GridManager")]
    public bool useMockData = true;
    [Tooltip("Terrain to read dimensions from (auto-detected if null)")]
    public Terrain targetTerrain;
    [Tooltip("Grid resolution for mock data")]
    public int mockGridWidth = 20;
    public int mockGridHeight = 20;
    [Tooltip("Seed for mock random demand values")]
    public int mockSeed = 42;
    [Tooltip("Regenerate mock data each update cycle")]
    public bool mockRandomizeEachCycle = false;

    private GridManager _grid;
    private Dictionary<int, GameObject> _activeCircles = new Dictionary<int, GameObject>();
    private Mesh _circleMesh;
    private float _updateTimer;
    private byte[] _mockDemandData;

    // Resolved grid dimensions for mock mode
    private Vector3 _resolvedOrigin;
    private float _resolvedWorldSizeX;
    private float _resolvedWorldSizeZ;
    private int _resolvedGridWidth;
    private int _resolvedGridHeight;

    void Start()
    {
        _grid = GridManager.Instance;
        _circleMesh = CreateCircleMesh();

        if (useMockData || _grid == null)
        {
            ResolveMockDimensions();
            GenerateMockData();
        }
    }

    void Update()
    {
        if (!useMockData)
        {
            if (_grid == null)
            {
                _grid = GridManager.Instance;
                if (_grid == null) return;
            }
        }

        _updateTimer += Time.deltaTime;
        if (_updateTimer < updateInterval) return;
        _updateTimer = 0f;

        if (useMockData || _grid == null)
        {
            if (mockRandomizeEachCycle)
                GenerateMockData();
            UpdateCirclesMock();
        }
        else
        {
            UpdateCircles();
        }
    }

    private void GenerateMockData()
    {
        int total = _resolvedGridWidth * _resolvedGridHeight;
        _mockDemandData = new byte[total];
        Random.InitState(mockSeed + (mockRandomizeEachCycle ? (int)(Time.time * 10) : 0));
        for (int i = 0; i < total; i++)
        {
            _mockDemandData[i] = (byte)Random.Range(0, 101);
        }
    }

    private void ResolveMockDimensions()
    {
        // Try to get dimensions from GridManager first
        if (_grid != null)
        {
            _resolvedGridWidth = _grid.Width;
            _resolvedGridHeight = _grid.Height;
            _resolvedOrigin = _grid.Origin;
            _resolvedWorldSizeX = _grid.CellSize.x * _grid.Width;
            _resolvedWorldSizeZ = _grid.CellSize.y * _grid.Height;
            return;
        }

        // Fall back to terrain
        if (targetTerrain == null)
            targetTerrain = Terrain.activeTerrain;

        if (targetTerrain != null)
        {
            Vector3 terrainSize = targetTerrain.terrainData.size;
            _resolvedOrigin = targetTerrain.transform.position;
            _resolvedWorldSizeX = terrainSize.x;
            _resolvedWorldSizeZ = terrainSize.z;
        }
        else
        {
            _resolvedOrigin = Vector3.zero;
            _resolvedWorldSizeX = 500f;
            _resolvedWorldSizeZ = 500f;
        }

        _resolvedGridWidth = mockGridWidth;
        _resolvedGridHeight = mockGridHeight;
    }

    private void UpdateCirclesMock()
    {
        int totalTiles = _resolvedGridWidth * _resolvedGridHeight;
        float cellSizeX = _resolvedWorldSizeX / _resolvedGridWidth;
        float cellSizeZ = _resolvedWorldSizeZ / _resolvedGridHeight;
        HashSet<int> activeTiles = new HashSet<int>();

        for (int i = 0; i < totalTiles; i++)
        {
            byte demand = _mockDemandData[i];

            if (demand > demandThreshold)
            {
                activeTiles.Add(i);

                if (!_activeCircles.TryGetValue(i, out GameObject circle))
                {
                    circle = CreateCircleObject(i);
                    _activeCircles[i] = circle;
                }

                float t = (demand - demandThreshold) / (float)(100 - demandThreshold);
                float radius = Mathf.Lerp(minCircleSize, maxCircleSize, t);
                circle.transform.localScale = new Vector3(radius, radius, radius);

                int x = i % _resolvedGridWidth;
                int y = i / _resolvedGridWidth;
                float posX = _resolvedOrigin.x + (x * cellSizeX) + (cellSizeX * 0.5f);
                float posZ = _resolvedOrigin.z + (y * cellSizeZ) + (cellSizeZ * 0.5f);
                float posY = GetTerrainHeight(posX, posZ) + heightOffset;
                circle.transform.position = new Vector3(posX, posY, posZ);
            }
        }

        RemoveInactiveCircles(activeTiles);
    }

    private void UpdateCircles()
    {
        int totalTiles = _grid.TotalTiles;
        HashSet<int> activeTiles = new HashSet<int>();

        for (int i = 0; i < totalTiles; i++)
        {
            TileData tile = _grid.GetTileData(i);
            byte demand = demandType == DemandType.InDemand ? tile.InDemand : tile.OutDemand;

            if (demand > demandThreshold)
            {
                activeTiles.Add(i);

                if (!_activeCircles.TryGetValue(i, out GameObject circle))
                {
                    circle = CreateCircleObject(i);
                    _activeCircles[i] = circle;
                }

                float t = (demand - demandThreshold) / (float)(100 - demandThreshold);
                float radius = Mathf.Lerp(minCircleSize, maxCircleSize, t);
                circle.transform.localScale = new Vector3(radius, radius, radius);

                _grid.GetXY(i, out int x, out int y);
                Vector3 worldPos = _grid.GridToWorld(x, y);
                circle.transform.position = new Vector3(worldPos.x, worldPos.y + heightOffset, worldPos.z);
            }
        }

        RemoveInactiveCircles(activeTiles);
    }

    private void RemoveInactiveCircles(HashSet<int> activeTiles)
    {
        List<int> toRemove = new List<int>();
        foreach (var kvp in _activeCircles)
        {
            if (!activeTiles.Contains(kvp.Key))
            {
                Destroy(kvp.Value);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (int key in toRemove)
        {
            _activeCircles.Remove(key);
        }
    }

    private GameObject CreateCircleObject(int tileIndex)
    {
        GameObject go = new GameObject($"DemandCircle_{demandType}_{tileIndex}");
        go.transform.SetParent(transform);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = _circleMesh;

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        if (circleMaterial != null)
            mr.sharedMaterial = circleMaterial;

        return go;
    }

    private float GetTerrainHeight(float worldX, float worldZ)
    {
        Vector3 rayStart = new Vector3(worldX, 1000f, worldZ);
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 1500f, terrainLayerMask))
        {
            return hit.point.y;
        }
        return 0f;
    }

    private Mesh CreateCircleMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "DemandCircle";

        Vector3[] vertices = new Vector3[segments + 1];
        int[] triangles = new int[segments * 3];
        Vector2[] uvs = new Vector2[segments + 1];

        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);

        float angleStep = 360f / segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Deg2Rad * angleStep * i;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);

            vertices[i + 1] = new Vector3(x, 0f, z);
            uvs[i + 1] = new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f);

            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = (i < segments - 1) ? i + 2 : 1;
            triangles[i * 3 + 2] = i + 1;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();

        return mesh;
    }

    void OnDestroy()
    {
        foreach (var kvp in _activeCircles)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        _activeCircles.Clear();

        if (_circleMesh != null)
            Destroy(_circleMesh);
    }
}
