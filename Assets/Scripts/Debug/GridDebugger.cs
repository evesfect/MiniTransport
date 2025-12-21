using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class GridDebugger : MonoBehaviour
{
    // --- UI State ---
    private Rect _windowRect = new Rect(20, 20, 320, 650); // Increased height slightly
    private bool _isCollapsed = false;
    private Vector2 _scrollPosition;

    private const float COLLAPSED_HEIGHT = 60f;
    private const float EXPANDED_HEIGHT = 650f;

    // Selection State
    private int _selectedIndex = -1;
    private int _selectedX = 0;
    private int _selectedY = 0;

    // Persistent Input Fields
    private string _inputIndex = "-1";
    private string _inputX = "0";
    private string _inputY = "0";

    // Edit State
    private int _editTraffic;
    private int _editPopulation;
    private int _editDemand;
    private int _editResRatio;
    private int _editComRatio;
    private int _editIndRatio;
    private EconomicClass _editEco;
    
    // Scheduling State
    private string _targetTimeInput = "8.0";

    // Visuals
    private bool _drawSelectedGizmo = true;

    private void OnGUI()
    {
        // 1. Apply Collapse Height Logic
        _windowRect.height = _isCollapsed ? COLLAPSED_HEIGHT : EXPANDED_HEIGHT;
        _windowRect = GUI.Window(100, _windowRect, DrawWindow, "");
    }

    private void DrawWindow(int id)
    {
        if (GridManager.Instance == null)
        {
            GUILayout.Label("Waiting for GridManager...");
            GUI.DragWindow();
            return;
        }

        GUILayout.BeginVertical();
        
        // 2. Draw Header
        DrawHeader();

        if (!_isCollapsed)
        {
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            DrawSelectionSection();
            GUILayout.Space(10);
            
            if (_selectedIndex >= 0 && _selectedIndex < GridManager.Instance.TotalTiles)
            {
                DrawTileInfoSection();
                GUILayout.Space(10);
                DrawBusStopSection();
                GUILayout.Space(10);
                DrawModifySection();
            }
            else
            {
                GUILayout.Label("No valid tile selected.");
            }

            GUILayout.EndScrollView();
        }

        GUILayout.EndVertical();
        GUI.DragWindow();
    }

    private void DrawHeader()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);

        if (GUILayout.Button(_isCollapsed ? "▶" : "▼", GUILayout.Width(25)))
            _isCollapsed = !_isCollapsed;

        GUILayout.Label("Grid Debugger", GUILayout.ExpandWidth(true));

        GUILayout.EndHorizontal();
    }

    private void DrawSelectionSection()
    {
        GUILayout.Label("Selection", GUI.skin.box);

        // --- X / Y Selection ---
        GUILayout.BeginHorizontal();
        GUILayout.Label("X:", GUILayout.Width(20));
        _inputX = GUILayout.TextField(_inputX, GUILayout.Width(40));
        GUILayout.Label("Y:", GUILayout.Width(20));
        _inputY = GUILayout.TextField(_inputY, GUILayout.Width(40));
        
        if (GUILayout.Button("Select XY"))
        {
            if (int.TryParse(_inputX, out int x) && int.TryParse(_inputY, out int y))
            {
                SelectTile(x, y);
            }
        }
        GUILayout.EndHorizontal();

        // --- Index Selection ---
        GUILayout.BeginHorizontal();
        GUILayout.Label("Index:", GUILayout.Width(45));
        _inputIndex = GUILayout.TextField(_inputIndex, GUILayout.Width(60));
        
        if (GUILayout.Button("Select Index"))
        {
            if (int.TryParse(_inputIndex, out int idx))
            {
                SelectTile(idx);
            }
        }
        GUILayout.EndHorizontal();
    }

    private void DrawTileInfoSection()
    {
        TileData data = GridManager.Instance.GetTileData(_selectedX, _selectedY);
        
        GUILayout.Label($"Current Data [Tile {_selectedIndex}]", GUI.skin.box);
        
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Traffic: {data.Traffic}%");
        GUILayout.Label($"Pop: {data.Population}");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Demand: {data.Demand}");
        GUILayout.Label($"Eco: {data.EcoClass}");
        GUILayout.EndHorizontal();

        GUILayout.Label($"Ratios (R/C/I): {data.ResidentialRatio}/{data.CommercialRatio}/{data.IndustrialRatio}");
    }

    private void DrawBusStopSection()
    {
        GUILayout.Label("Bus Stops in Tile", GUI.skin.box);
        
        var stops = GridManager.Instance.GetStopsInTile(_selectedIndex);
        if (stops != null && stops.Count > 0)
        {
            foreach (var stop in stops)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"🚏 {stop.name} ({stop.stopID})");
                
                // Camera Focus Button
                if (GUILayout.Button("Go", GUILayout.Width(30)))
                {
                    var rtsCam = FindFirstObjectByType<RTSCameraController>();
                    if (rtsCam != null)
                    {
                        rtsCam.SetTargetFocusPoint(stop.transform.position);
                    }
                    else if (Camera.main != null)
                    {
                        Camera.main.transform.position = stop.transform.position + Vector3.up * 10f - Vector3.forward * 10f;
                    }
                }
                GUILayout.EndHorizontal();
            }
        }
        else
        {
            GUILayout.Label("(None)");
        }
    }

    private void DrawModifySection()
    {
        GUILayout.Label("Schedule Update (Server Only)", GUI.skin.box);

        if (!NetworkManager.Singleton.IsServer)
        {
            GUI.color = Color.red;
            GUILayout.Label("You must be Host/Server to schedule updates.");
            GUI.color = Color.white;
            return;
        }

        // --- Inputs ---
        GUILayout.BeginHorizontal();
        GUILayout.Label("Traffic:");
        _editTraffic = IntField(_editTraffic);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Population:");
        _editPopulation = IntField(_editPopulation);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Demand:");
        _editDemand = IntField(_editDemand);
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        // --- Immediate (ASAP) ---
        if (GUILayout.Button("Apply ASAP (Lookahead)"))
        {
            ScheduleChanges(false);
        }

        GUILayout.Space(5);

        // --- Specific Time ---
        float currentTime = 0f;
        if (SimulationTimeManager.Instance != null) 
            currentTime = SimulationTimeManager.Instance.CurrentTimeOfDay;

        GUILayout.Label($"Current Game Time: {currentTime:F2}");
        
        GUILayout.BeginHorizontal();
        GUILayout.Label("Target Time:", GUILayout.Width(80));
        _targetTimeInput = GUILayout.TextField(_targetTimeInput);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Schedule at Specific Time"))
        {
            ScheduleChanges(true);
        }
    }

    private void SelectTile(int x, int y)
    {
        if (GridManager.Instance == null) return;
        _selectedIndex = GridManager.Instance.GetIndex(x, y);
        _selectedX = x;
        _selectedY = y;
        
        // Update inputs to match selection
        _inputIndex = _selectedIndex.ToString();
        _inputX = _selectedX.ToString();
        _inputY = _selectedY.ToString();

        SyncEditValues();
    }

    private void SelectTile(int index)
    {
        if (GridManager.Instance == null) return;
        GridManager.Instance.GetXY(index, out int x, out int y);
        _selectedIndex = index;
        _selectedX = x;
        _selectedY = y;

        // Update inputs to match selection
        _inputIndex = _selectedIndex.ToString();
        _inputX = _selectedX.ToString();
        _inputY = _selectedY.ToString();

        SyncEditValues();
    }

    private void SyncEditValues()
    {
        TileData data = GridManager.Instance.GetTileData(_selectedX, _selectedY);
        _editTraffic = data.Traffic;
        _editPopulation = data.Population;
        _editDemand = data.Demand;
        _editResRatio = data.ResidentialRatio;
        _editComRatio = data.CommercialRatio;
        _editIndRatio = data.IndustrialRatio;
        _editEco = data.EcoClass;
    }

    private void ScheduleChanges(bool useSpecificTime)
    {
        TileData current = GridManager.Instance.GetTileData(_selectedX, _selectedY);
        TileUpdateFlags mask = TileUpdateFlags.None;

        if (_editTraffic != current.Traffic) mask |= TileUpdateFlags.Traffic;
        if (_editPopulation != current.Population) mask |= TileUpdateFlags.Population;
        if (_editDemand != current.Demand) mask |= TileUpdateFlags.Demand;

        // Construct new data
        TileData newData = current;
        newData.Traffic = (byte)Mathf.Clamp(_editTraffic, 0, 100);
        newData.Population = (ushort)Mathf.Clamp(_editPopulation, 0, 65535);
        newData.Demand = (byte)Mathf.Clamp(_editDemand, 0, 100);

        if (mask != TileUpdateFlags.None)
        {
            if (useSpecificTime)
            {
                if (float.TryParse(_targetTimeInput, out float targetTime))
                {
                    // Call the NEW method for specific timing
                    GridManager.Instance.ScheduleTileUpdateAtGameTime(_selectedIndex, newData, mask, targetTime);
                    Debug.Log($"[Debugger] Scheduled SPECIFIC update for Tile {_selectedIndex} at {targetTime:F2}. Mask: {mask}");
                }
                else
                {
                    Debug.LogError("[Debugger] Invalid Target Time format.");
                }
            }
            else
            {
                // Call the standard ASAP method
                GridManager.Instance.ScheduleTileUpdate(_selectedIndex, newData, mask);
                Debug.Log($"[Debugger] Scheduled ASAP update for Tile {_selectedIndex}. Mask: {mask}");
            }
        }
        else
        {
            Debug.LogWarning("[Debugger] No changes detected to schedule.");
        }
    }

    private int IntField(int val)
    {
        string s = GUILayout.TextField(val.ToString());
        int.TryParse(s, out int res);
        return res;
    }

    private void OnDrawGizmos()
    {
        if (_drawSelectedGizmo && _selectedIndex >= 0 && GridManager.Instance != null)
        {
            Vector3 worldPos = GridManager.Instance.GridToWorld(_selectedX, _selectedY);
            Vector2 size = GridManager.Instance.CellSize;
            
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(worldPos, new Vector3(size.x, 10f, size.y));
            Gizmos.color = new Color(1, 0.92f, 0.016f, 0.3f);
            Gizmos.DrawCube(worldPos, new Vector3(size.x, 0.5f, size.y));
        }
    }
}