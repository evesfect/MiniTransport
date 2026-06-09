using UnityEngine;
using System.Collections.Generic;

public class TrafficHeatmapVisualizer : MonoBehaviour
{
    [Header("Threshold")]
    [Tooltip("Tiles with traffic at or below this value won't show")]
    [Range(0, 100)]
    public byte trafficThreshold = 0;

    [Header("Colors")]
    [Tooltip("Color at lowest visible traffic (threshold+1)")]
    public Color lowTrafficColor = new Color(0f, 1f, 0f, 0.4f);
    [Tooltip("Color at mid traffic")]
    public Color midTrafficColor = new Color(1f, 1f, 0f, 0.6f);
    [Tooltip("Color at max traffic (100)")]
    public Color highTrafficColor = new Color(1f, 0f, 0f, 0.8f);

    [Header("Tile Visuals")]
    [Tooltip("Number of segments for the tile quad mesh")]
    public int segments = 4;
    [Tooltip("Scale multiplier relative to cell size (1 = full cell)")]
    [Range(0.1f, 1f)]
    public float cellFillRatio = 0.9f;

    [Header("Material")]
    [Tooltip("Assign your own material (use an unlit shader)")]
    public Material heatmapMaterial;

    [Header("Height")]
    [Tooltip("Fixed Y position for the entire heatmap grid")]
    public float gridHeight = 5f;

    [Header("Transparency")]
    [Range(0f, 1f)]
    [Tooltip("Global transparency multiplier for heatmap tiles")]
    public float transparency = 0.7f;

    [Header("Update Rate")]
    [Tooltip("Seconds between traffic checks")]
    public float updateInterval = 1f;

    [Header("Mock Data (temp)")]
    [Tooltip("Use mock data instead of GridManager")]
    public bool useMockData = true;
    [Tooltip("Terrain to read dimensions from (auto-detected if null)")]
    public Terrain targetTerrain;
    [Tooltip("Grid resolution for mock data")]
    public int mockGridWidth = 20;
    public int mockGridHeight = 20;
    [Tooltip("Seed for mock random traffic values")]
    public int mockSeed = 99;
    [Tooltip("Regenerate mock data each update cycle")]
    public bool mockRandomizeEachCycle = false;

    private GridManager _grid;
    private Dictionary<int, GameObject> _activeTiles = new Dictionary<int, GameObject>();
    private Dictionary<int, Material> _tileMaterials = new Dictionary<int, Material>();
    private Mesh _quadMesh;
    private float _updateTimer;
    private byte[] _mockTrafficData;

    private Vector3 _resolvedOrigin;
    private float _resolvedWorldSizeX;
    private float _resolvedWorldSizeZ;
    private int _resolvedGridWidth;
    private int _resolvedGridHeight;
    private float _resolvedCellSizeX;
    private float _resolvedCellSizeZ;

    void Start()
    {
        _grid = GridManager.Instance;
        _quadMesh = CreateQuadMesh();

        if (useMockData || _grid == null)
        {
            ResolveMockDimensions();
            GenerateMockData();
        }
        else
        {
            _resolvedGridWidth = _grid.Width;
            _resolvedGridHeight = _grid.Height;
            _resolvedOrigin = _grid.Origin;
            _resolvedCellSizeX = _grid.CellSize.x;
            _resolvedCellSizeZ = _grid.CellSize.y;
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
            UpdateHeatmapMock();
        }
        else
        {
            UpdateHeatmap();
        }
    }

    private void ResolveMockDimensions()
    {
        if (_grid != null)
        {
            _resolvedGridWidth = _grid.Width;
            _resolvedGridHeight = _grid.Height;
            _resolvedOrigin = _grid.Origin;
            _resolvedWorldSizeX = _grid.CellSize.x * _grid.Width;
            _resolvedWorldSizeZ = _grid.CellSize.y * _grid.Height;
        }
        else
        {
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

        _resolvedCellSizeX = _resolvedWorldSizeX / _resolvedGridWidth;
        _resolvedCellSizeZ = _resolvedWorldSizeZ / _resolvedGridHeight;
    }

    private void GenerateMockData()
    {
        int total = _resolvedGridWidth * _resolvedGridHeight;
        _mockTrafficData = new byte[total];
        Random.InitState(mockSeed + (mockRandomizeEachCycle ? (int)(Time.time * 10) : 0));
        for (int i = 0; i < total; i++)
        {
            _mockTrafficData[i] = (byte)Random.Range(0, 101);
        }
    }

    private void UpdateHeatmapMock()
    {
        int totalTiles = _resolvedGridWidth * _resolvedGridHeight;
        HashSet<int> activeTiles = new HashSet<int>();

        for (int i = 0; i < totalTiles; i++)
        {
            byte traffic = _mockTrafficData[i];
            UpdateTile(i, traffic, activeTiles);
        }

        RemoveInactiveTiles(activeTiles);
    }

    private void UpdateHeatmap()
    {
        int totalTiles = _grid.TotalTiles;
        HashSet<int> activeTiles = new HashSet<int>();

        for (int i = 0; i < totalTiles; i++)
        {
            TileData tile = _grid.GetTileData(i);
            UpdateTile(i, tile.Traffic, activeTiles);
        }

        RemoveInactiveTiles(activeTiles);
    }

    private void UpdateTile(int i, byte traffic, HashSet<int> activeTiles)
    {
        if (traffic > trafficThreshold)
        {
            activeTiles.Add(i);

            if (!_activeTiles.TryGetValue(i, out GameObject tileObj))
            {
                tileObj = CreateTileObject(i);
                _activeTiles[i] = tileObj;
            }

            // Position
            int x = i % _resolvedGridWidth;
            int y = i / _resolvedGridWidth;
            float posX = _resolvedOrigin.x + (x * _resolvedCellSizeX) + (_resolvedCellSizeX * 0.5f);
            float posZ = _resolvedOrigin.z + (y * _resolvedCellSizeZ) + (_resolvedCellSizeZ * 0.5f);
            float posY = gridHeight;
            tileObj.transform.position = new Vector3(posX, posY, posZ);

            // Scale to cell size
            float scaleX = _resolvedCellSizeX * cellFillRatio;
            float scaleZ = _resolvedCellSizeZ * cellFillRatio;
            tileObj.transform.localScale = new Vector3(scaleX, 1f, scaleZ);

            // Color
            Color color = GetTrafficColor(traffic);
            ApplyColor(tileObj, i, color);
        }
    }

    private Color GetTrafficColor(byte traffic)
    {
        float t = (traffic - trafficThreshold) / (float)(100 - trafficThreshold);
        Color color;
        if (t < 0.5f)
            color = Color.Lerp(lowTrafficColor, midTrafficColor, t * 2f);
        else
            color = Color.Lerp(midTrafficColor, highTrafficColor, (t - 0.5f) * 2f);
        color.a *= transparency;
        return color;
    }

    private void ApplyColor(GameObject tileObj, int index, Color color)
    {
        MeshRenderer mr = tileObj.GetComponent<MeshRenderer>();
        if (mr == null) return;

        if (!_tileMaterials.TryGetValue(index, out Material mat))
        {
            mat = new Material(heatmapMaterial);
            // Enable transparency on URP shaders
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            _tileMaterials[index] = mat;
            mr.material = mat;
        }

        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color", color);
    }

    private void RemoveInactiveTiles(HashSet<int> activeTiles)
    {
        List<int> toRemove = new List<int>();
        foreach (var kvp in _activeTiles)
        {
            if (!activeTiles.Contains(kvp.Key))
            {
                Destroy(kvp.Value);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (int key in toRemove)
        {
            _activeTiles.Remove(key);
            if (_tileMaterials.TryGetValue(key, out Material mat))
            {
                Destroy(mat);
                _tileMaterials.Remove(key);
            }
        }
    }

    private GameObject CreateTileObject(int tileIndex)
    {
        GameObject go = new GameObject($"TrafficHeat_{tileIndex}");
        go.transform.SetParent(transform);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = _quadMesh;

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        return go;
    }

    private Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "HeatmapQuad";

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f,  0.5f),
            new Vector3(-0.5f, 0f,  0.5f)
        };

        int[] triangles = new int[]
        {
            0, 2, 1,
            0, 3, 2
        };

        Vector2[] uvs = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();

        return mesh;
    }


    void OnDestroy()
    {
        foreach (var kvp in _activeTiles)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        _activeTiles.Clear();

        foreach (var kvp in _tileMaterials)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        _tileMaterials.Clear();

        if (_quadMesh != null)
            Destroy(_quadMesh);
    }
}
