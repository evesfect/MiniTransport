using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class InventoryDebugger : MonoBehaviour
{
    // --- UI State ---
    private Rect _windowRect = new Rect(20, 20, 420, 650);
    private bool _isCollapsed = true;

    private Vector2 _scroll;

    // Editing state
    private string _selectedItemID;
    private int _editAmount = 1;

    private const float COLLAPSED_HEIGHT = 60f;
    private const float EXPANDED_HEIGHT = 850f;

    private void OnGUI()
    {
        _windowRect.height = _isCollapsed ? COLLAPSED_HEIGHT : EXPANDED_HEIGHT;
        _windowRect = GUI.Window(10, _windowRect, DrawWindow, "");
    }

    private void DrawWindow(int id)
    {
        if (InventoryManager.Instance == null)
        {
            GUILayout.Label("Waiting for InventoryManager...");
            GUI.DragWindow();
            return;
        }

        GUILayout.BeginVertical();

        DrawHeader();

        if (!_isCollapsed)
        {
            DrawNetworkInfo();
            GUILayout.Space(5);
            DrawInventoryList();
            GUILayout.Space(10);
            DrawItemEditor();
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

        GUILayout.Label("Inventory Debugger", GUILayout.ExpandWidth(true));

        GUILayout.EndHorizontal();
    }

    #endregion

    #region Network Info

    private void DrawNetworkInfo()
    {
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.Label("Network State");

        GUILayout.Label($"Is Server: {NetworkManager.Singleton.IsServer}");
        GUILayout.Label($"Is Client: {NetworkManager.Singleton.IsClient}");
        GUILayout.Label($"Is Host: {NetworkManager.Singleton.IsHost}");

        GUILayout.EndVertical();
    }

    #endregion

    #region Inventory List

    private void DrawInventoryList()
    {
        GUILayout.Label("Inventory Contents", GUI.skin.box);

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(260));

        var manager = InventoryManager.Instance;
        var items = manager.allAvailableItems;

        if (items == null || items.Length == 0)
        {
            GUILayout.Label("(No items configured)");
        }
        else
        {
            foreach (var item in items)
            {
                int qty = manager.GetItemQuantity(item.ItemID);

                GUILayout.BeginHorizontal(GUI.skin.box);

                Color prev = GUI.color;
                GUI.color = qty > 0 ? Color.green : Color.gray;

                if (GUILayout.Button(item.ItemID, GUILayout.Width(120)))
                {
                    SelectItem(item);
                }

                GUI.color = prev;

                GUILayout.Label($"x{qty}", GUILayout.Width(50));
                GUILayout.Label(item.Category.ToString(), GUILayout.Width(80));

                GUILayout.EndHorizontal();
            }
        }

        GUILayout.EndScrollView();
    }

    #endregion

    #region Item Editor

    private void DrawItemEditor()
    {
        GUILayout.Label("Item Editor", GUI.skin.box);

        if (string.IsNullOrEmpty(_selectedItemID))
        {
            GUILayout.Label("Select an item to edit.");
            return;
        }

        var item = InventoryManager.Instance.allAvailableItems
            .FirstOrDefault(i => i.ItemID == _selectedItemID);

        if (item == null)
        {
            GUILayout.Label("Item not found.");
            return;
        }

        GUILayout.Label($"ID: {item.ItemID}");
        GUILayout.Label($"Name: {item.DisplayName}");
        GUILayout.Label($"Category: {item.Category}");
        GUILayout.Label($"Cost: {item.Cost}");

        GUILayout.Label("Description:");
        GUILayout.TextArea(item.Description, GUILayout.Height(60));

        GUILayout.Space(5);

        _editAmount = DrawIntField("Amount", _editAmount);

        GUILayout.BeginHorizontal();

        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Add"))
        {
            InventoryManager.Instance.IncreaseItemQuantity(item.ItemID, _editAmount);
        }

        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("Remove"))
        {
            InventoryManager.Instance.DecreaseItemQuantity(item.ItemID, _editAmount);
        }

        GUI.backgroundColor = Color.white;

        GUILayout.EndHorizontal();
    }

    #endregion

    #region Helpers

    private void SelectItem(InventoryItemData item)
    {
        _selectedItemID = item.ItemID;
        _editAmount = 1;
    }

    private int DrawIntField(string label, int value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(80));

        string str = GUILayout.TextField(value.ToString());
        int.TryParse(str, out value);

        GUILayout.EndHorizontal();
        return Mathf.Max(0, value);
    }

    #endregion
}
