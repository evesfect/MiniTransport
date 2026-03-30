using UnityEngine;
using System.Linq;

public class FleetDebugger : MonoBehaviour
{
    // --- UI State ---
    private Rect _windowRect = new Rect(360, 20, 420, 650);
    private bool _isCollapsed = true;

    // Editing state
    private string _busID = "";
    private string _depotID = "";
    private BusSchedule _schedule = new BusSchedule();
    private string _capacityStr = "30";

    private Vector2 _scroll;

    private const float COLLAPSED_HEIGHT = 60f;
    private const float EXPANDED_HEIGHT = 650f;

    private void OnGUI()
    {
        _windowRect.height = _isCollapsed ? COLLAPSED_HEIGHT : EXPANDED_HEIGHT;
        _windowRect = GUI.Window(2, _windowRect, DrawWindow, "");
    }

    private void DrawWindow(int id)
    {
        if (FleetManager.Instance == null)
        {
            GUILayout.Label("Waiting for FleetManager...");
            GUI.DragWindow();
            return;
        }

        GUILayout.BeginVertical();
        DrawHeader();

        if (!_isCollapsed)
        {
            DrawFleetList();
            GUILayout.Space(10);
            DrawEditor();
        }

        GUILayout.EndVertical();
        GUI.DragWindow();
    }

    #region Header

    private void DrawHeader()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);

        if (GUILayout.Button(_isCollapsed ? "▶" : "▼", GUILayout.Width(25)))
            _isCollapsed = !_isCollapsed;

        GUILayout.Label("Fleet Debugger", GUILayout.ExpandWidth(true));

        GUILayout.EndHorizontal();
    }

    #endregion

    #region Fleet List

    private void DrawFleetList()
    {
        GUILayout.Label("Active Fleet (Global)", GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Bus ID", GUILayout.Width(120));
        GUILayout.Label("Depot", GUILayout.Width(70));
        GUILayout.Label("Pax", GUILayout.Width(40)); // Passengers
        GUILayout.Label("Status", GUILayout.Width(80));
        GUILayout.EndHorizontal();

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(250));

        if (FleetManager.Instance.allBuses.Count == 0)
        {
            GUILayout.Label("(No buses)");
        }
        else
        {
            foreach (var bus in FleetManager.Instance.allBuses)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);

                // Determine state via FleetManager lookup instead of cached enum
                bool isActive = FleetManager.Instance.IsBusActive(bus.BusID);

                string passengerText = "-";
                if (isActive)
                {
                    GameObject busObj = FleetManager.Instance.GetActiveBus(bus.BusID);
                    if (busObj != null)
                    {
                        BusDriver driver = busObj.GetComponent<BusDriver>();
                        if (driver != null)
                        {
                            // Reads the property we added to BusDriver.cs
                            passengerText = driver.PassengerCount.ToString();
                        }
                    }
                }

                Color prev = GUI.color;
                GUI.color = isActive ? Color.green : Color.cyan;

                if (GUILayout.Button(bus.BusID, GUILayout.Width(120)))
                {
                    LoadBus(bus);
                }

                GUILayout.Label(bus.AssignedDepotID, GUILayout.Width(70));

                GUILayout.Label(passengerText, GUILayout.Width(40));

                GUILayout.Label(isActive ? "Active" : "Depot", GUILayout.Width(80));

                GUI.color = prev;

                GUILayout.EndHorizontal();
            }
        }

        GUILayout.EndScrollView();
    }

    #endregion

    #region Editor

    private void DrawEditor()
    {
        GUILayout.Label("Bus Editor", GUI.skin.box);

        _busID = DrawField("Bus ID", _busID);
        _depotID = DrawField("Depot ID", _depotID);

        GUILayout.Label("Schedule (JSON)");
        string scheduleJson = JsonUtility.ToJson(_schedule, true);
        scheduleJson = GUILayout.TextArea(scheduleJson, GUILayout.Height(80));
        JsonUtility.FromJsonOverwrite(scheduleJson, _schedule);
        _capacityStr = DrawField("Capacity", _capacityStr);

        GUILayout.Space(5);

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Create"))
        {
            ushort cap = 30;
            ushort.TryParse(_capacityStr, out cap);

            // UPDATED CALL: Pass capacity to the new signature
            FleetManager.Instance.CreateBusClient(_busID, _depotID, _schedule, cap);
        }

        if (GUILayout.Button("Update"))
        {
            BusData entry = FleetManager.Instance.allBuses
                .FirstOrDefault(b => b.BusID == _busID);

            if (entry != null)
            {
                entry.AssignedDepotID = _depotID;
                entry.Schedule = _schedule;

                if (ushort.TryParse(_capacityStr, out ushort newCap))
                {
                    entry.Capacity = newCap;
                }
                FleetManager.Instance.UpdateBusClient(entry);
            }
        }

        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("Delete"))
        {
            FleetManager.Instance.DeleteBusClient(_busID);
        }
        GUI.backgroundColor = Color.white;

        GUILayout.EndHorizontal();
    }

    private string DrawField(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(80));
        value = GUILayout.TextField(value);
        GUILayout.EndHorizontal();
        return value;
    }

    #endregion

    #region Helpers

    private void LoadBus(BusData bus)
    {
        _busID = bus.BusID;
        _depotID = bus.AssignedDepotID;
        _schedule = bus.Schedule;
        _capacityStr = bus.Capacity.ToString();
    }

    #endregion
}