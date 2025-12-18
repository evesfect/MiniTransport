using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class InventoryDebugger : MonoBehaviour
{
    // --- UI State ---
    // Start with a reasonable default, but height will be overridden dynamically
    private Rect _windowRect = new Rect(20, 20, 420, 100);
    private bool _isCollapsed = true;

    private Vector2 _scroll;

    // Editing state
    private string _selectedItemID;
    private int _editAmount = 1;

    private const float COLLAPSED_HEIGHT = 60f;

    private void OnGUI()
    {
        // --- DYNAMIC HEIGHT CALCULATION ---
        float targetHeight = COLLAPSED_HEIGHT;

        if (!_isCollapsed && InventoryManager.Instance != null)
        {
            // 1. Calculate Static Content Height (Header + NetworkInfo + Editor + Spacing)
            // Header: ~30, Network: ~85, Editor: ~280, Spacing/Padding: ~25
            float staticContentHeight = 420f; 

            // 2. Calculate List Height based on item count
            int itemCount = 0;
            if (InventoryManager.Instance.allAvailableItems != null)
            {
                itemCount = InventoryManager.Instance.allAvailableItems.Length;
            }

            // 3. Limit the list height so the window doesn't explode off-screen.
            // Min 50px, Max 300px (scrolls if larger)
            float listHeight = Mathf.Clamp(itemCount * 36f + 10f, 50f, 300f);

            targetHeight = staticContentHeight + listHeight;
        }

        // Apply the calculated height
        _windowRect.height = targetHeight;
        
        // Render the window
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
            
            // Pass the calculated list height to the Draw function so the ScrollView matches
            int itemCount = InventoryManager.Instance.allAvailableItems != null ? InventoryManager.Instance.allAvailableItems.Length : 0;
            float listHeight = Mathf.Clamp(itemCount * 36f + 10f, 50f, 300f);
            
            DrawInventoryList(listHeight);
            
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

        string serverStatus = NetworkManager.Singleton.IsServer ? "Yes" : "No";
        string clientStatus = NetworkManager.Singleton.IsClient ? "Yes" : "No";
        string hostStatus = NetworkManager.Singleton.IsHost ? "Yes" : "No";

        GUILayout.Label($"Is Server: {serverStatus} | Is Client: {clientStatus} | Is Host: {hostStatus}");

        GUILayout.EndVertical();
    }

    #endregion

    #region Inventory List

    private void DrawInventoryList(float height)
    {
        GUILayout.Label("Inventory Contents", GUI.skin.box);

        // Use the calculated height for the ScrollView
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(height));

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
            GUILayout.Label("Select an item above to edit.");
            return;
        }

        var item = InventoryManager.Instance.allAvailableItems
            .FirstOrDefault(i => i.ItemID == _selectedItemID);

        if (item == null)
        {
            GUILayout.Label("Item not found (Refresh list).");
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