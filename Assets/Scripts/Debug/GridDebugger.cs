using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class GridDebugger : MonoBehaviour
{
    // --- UI State ---
    private Rect _windowRect = new Rect(20, 20, 340, 700);
    private bool _isCollapsed = false;
    private Vector2 _scrollPosition;

    private const float COLLAPSED_HEIGHT = 60f;
    private const float EXPANDED_HEIGHT = 700f;

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
    
    // Structure Edit State
    private int _editResRatio;
    private int _editComRatio;
    private int _editIndRatio;
    private EconomicClass _editEco;
    
    // Visuals
    private bool _drawSelectedGizmo = true;

    private void OnGUI()
    {
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
        
        // Dynamic Data
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Traffic: {data.Traffic}%");
        GUILayout.Label($"Pop: {data.Population}");
        GUILayout.EndHorizontal();
        GUILayout.Label($"Demand: {data.Demand}");

        // Structure Data
        GUILayout.Space(5);
        GUILayout.Label($"<b>Structure</b>");
        GUILayout.Label($"Eco: {data.EcoClass}");
        GUILayout.Label($"Res Ratio: {data.ResidentialRatio}%");
        GUILayout.Label($"Com Ratio: {data.CommercialRatio}%");
        GUILayout.Label($"Ind Ratio: {data.IndustrialRatio}%");
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
                // Accessing name directly since we have the reference
                GUILayout.Label($"🚏 {stop.name}"); 
                
                if (GUILayout.Button("Go", GUILayout.Width(30)))
                {
                    FocusCamera(stop.transform.position);
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
        GUILayout.Label("Schedule Update", GUI.skin.box);

        if (NetworkManager.Singleton == null)
        {
            GUI.color = Color.yellow;
            GUILayout.Label("NetworkManager not found.");
            GUI.color = Color.white;
            return;
        }
        
        if (!NetworkManager.Singleton.IsServer)
        {
            GUI.color = Color.red;
            GUILayout.Label("You must be Host/Server to schedule updates.");
            GUI.color = Color.white;
            return;
        }

        // --- Basic Metrics ---
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
        GUILayout.Label("--- Structure ---");

        // --- Economic Class ---
        GUILayout.BeginHorizontal();
        GUILayout.Label("Eco Class:");
        if (GUILayout.Button(_editEco.ToString()))
        {
            // Cycle between Low(0), Medium(1), High(2)
            _editEco = (EconomicClass)(((int)_editEco + 1) % 3);
        }
        GUILayout.EndHorizontal();

        // --- Ratios ---
        GUILayout.BeginHorizontal();
        GUILayout.Label("Res %:");
        _editResRatio = IntField(_editResRatio);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Com %:");
        _editComRatio = IntField(_editComRatio);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Ind %:");
        _editIndRatio = IntField(_editIndRatio);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Normalize Ratios to 100%"))
        {
            NormalizeRatios();
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Schedule Update (Lookahead)"))
        {
            ScheduleChanges();
        }
    }

    private void SelectTile(int x, int y)
    {
        if (GridManager.Instance == null) return;
        _selectedIndex = GridManager.Instance.GetIndex(x, y);
        _selectedX = x;
        _selectedY = y;
        
        UpdateInputStrings();
        SyncEditValues();
    }

    private void SelectTile(int index)
    {
        if (GridManager.Instance == null) return;
        if (index < 0 || index >= GridManager.Instance.TotalTiles) return;

        GridManager.Instance.GetXY(index, out int x, out int y);
        _selectedIndex = index;
        _selectedX = x;
        _selectedY = y;

        UpdateInputStrings();
        SyncEditValues();
    }

    private void UpdateInputStrings()
    {
        _inputIndex = _selectedIndex.ToString();
        _inputX = _selectedX.ToString();
        _inputY = _selectedY.ToString();
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

    private void NormalizeRatios()
    {
        float total = _editResRatio + _editComRatio + _editIndRatio;
        if (total <= 0) return;

        _editResRatio = Mathf.RoundToInt((_editResRatio / total) * 100f);
        _editComRatio = Mathf.RoundToInt((_editComRatio / total) * 100f);
        _editIndRatio = 100 - (_editResRatio + _editComRatio);
    }

    private void ScheduleChanges()
    {
        TileData current = GridManager.Instance.GetTileData(_selectedX, _selectedY);
        TileUpdateFlags mask = TileUpdateFlags.None;

        // 1. Check Standard Fields
        if (_editTraffic != current.Traffic) mask |= TileUpdateFlags.Traffic;
        if (_editPopulation != current.Population) mask |= TileUpdateFlags.Population;
        if (_editDemand != current.Demand) mask |= TileUpdateFlags.Demand;

        // 2. Check Structure Fields
        bool ratiosChanged = (_editResRatio != current.ResidentialRatio) || 
                             (_editComRatio != current.CommercialRatio) || 
                             (_editIndRatio != current.IndustrialRatio);
        
        if (ratiosChanged) mask |= TileUpdateFlags.Ratios;
        
        if (_editEco != current.EcoClass) mask |= TileUpdateFlags.Economy;

        // Construct new data
        TileData newData = current;
        newData.Traffic = (byte)Mathf.Clamp(_editTraffic, 0, 100);
        newData.Population = (ushort)Mathf.Clamp(_editPopulation, 0, 65535);
        newData.Demand = (byte)Mathf.Clamp(_editDemand, 0, 100);
        
        // Assign Structure directly
        newData.ResidentialRatio = (byte)_editResRatio;
        newData.CommercialRatio = (byte)_editComRatio;
        newData.IndustrialRatio = (byte)_editIndRatio;
        newData.EcoClass = _editEco;

        if (mask != TileUpdateFlags.None)
        {
            // Call the ONLY valid overload: (index, data, mask)
            GridManager.Instance.ScheduleTileUpdate(_selectedIndex, newData, mask);
            Debug.Log($"[Debugger] Scheduled update for Tile {_selectedIndex}. Mask: {mask}");
        }
        else
        {
            Debug.LogWarning("[Debugger] No changes detected to schedule.");
        }
    }

    private int IntField(int val)
    {
        string s = GUILayout.TextField(val.ToString(), GUILayout.Width(50));
        s = System.Text.RegularExpressions.Regex.Replace(s, "[^0-9]", "");
        if (int.TryParse(s, out int res)) return res;
        return 0;
    }
    
    private void FocusCamera(Vector3 position)
    {
        var rtsCam = FindFirstObjectByType<RTSCameraController>();
        if (rtsCam != null)
        {
            // Use the public method to update the camera's target focus point
            rtsCam.SetTargetFocusPoint(position);
        }
        else if (Camera.main != null)
        {
            Camera.main.transform.position = position + Vector3.up * 20f - Vector3.forward * 20f;
            Camera.main.transform.LookAt(position);
        }
    }

    private void OnDrawGizmos()
    {
        if (_drawSelectedGizmo && _selectedIndex >= 0 && GridManager.Instance != null)
        {
            Vector3 worldPos = GridManager.Instance.GridToWorld(_selectedX, _selectedY);
            Vector2 size = GridManager.Instance.CellSize;
            
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(worldPos, new Vector3(size.x, 20f, size.y));
            Gizmos.color = new Color(1, 0.92f, 0.016f, 0.3f);
            Gizmos.DrawCube(worldPos, new Vector3(size.x, 0.5f, size.y));
        }
    }
}