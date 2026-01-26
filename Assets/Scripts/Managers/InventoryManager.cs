using UnityEngine;
using System.Collections.Generic;
using System;
using Unity.Netcode;
using System.Linq;
using System.IO;

public class InventoryManager : NetworkBehaviour
{
    public static InventoryManager Instance { get; private set; }

    //Key:InventoryItemData.ItemID (string)
    //Value: Quantity
    private Dictionary<string, int> inventoryDatabase = new Dictionary<string, int>();

    [Header("Configure")]
    [Tooltip("All available InventoryItemData ScriptableObjects to load into the system.")]
    public InventoryItemData[] allAvailableItems;

    // --- Events to notify UI Logic of changes ---
    public event Action<string, int> OnItemQuantityChanged;

    #if UNITY_EDITOR
    private string SavePath => Path.Combine(Application.dataPath, "inventory.json");
#else
    private string SavePath => Path.Combine(Application.persistentDataPath, "inventory.json");
#endif

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            LoadInventory();
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
        else
        {
            inventoryDatabase.Clear();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnApplicationQuit()
    {
        if(IsServer) SaveInventory();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            string json = SerializeInventory(); 
            SyncInventoryRpc(json, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }

    private void InitializeInventory()
    {
        if (allAvailableItems != null)
        {
            foreach (var item in allAvailableItems)
            {
                if (!inventoryDatabase.ContainsKey(item.ItemID))
                {
                    inventoryDatabase.Add(item.ItemID, 0);
                }
            }
            Debug.Log($"[InventoryManager] Initialized {inventoryDatabase.Count} items on Server.");
        }
    }

    [ContextMenu("Save Inventory")]
    public void SaveInventory()
    {
        string json = SerializeInventory();
        File.WriteAllText(SavePath, json);
        Debug.Log($"[InventoryManager] Saved to {SavePath}");
    }

    [ContextMenu("Load Inventory")]
    public void LoadInventory()
    {
        // First setup the keys from the ScriptableObjects
        InitializeInventory(); 

        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);
                InventorySaveData data = JsonUtility.FromJson<InventorySaveData>(json);
                if (data != null && data.Items != null)
                {
                    foreach (var entry in data.Items)
                    {
                        // Only load if the item ID is still valid in our config
                        if (inventoryDatabase.ContainsKey(entry.ID))
                        {
                            inventoryDatabase[entry.ID] = entry.Count;
                        }
                    }
                    Debug.Log($"[InventoryManager] Loaded {data.Items.Count} items from disk.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[InventoryManager] Load failed: {e.Message}");
            }
        }
    }

    public void IncreaseItemQuantity(string itemID, int amount)
    {
        if (amount <= 0) return;

        if (IsServer)
        {
            ModifyQuantityInternal(itemID, amount);
        }
        else
        {
            RequestItemChangeRpc(itemID, amount);
        }
    }

    public bool DecreaseItemQuantity(string itemID, int amount)
    {
        if (amount <= 0) return false;

        if (IsServer)
        {
            return ModifyQuantityInternal(itemID, -amount);
        }
        else
        {
            RequestItemChangeRpc(itemID, -amount);
            return true; 
        }
    }

    public int GetItemQuantity(string itemID)
    {
        if (inventoryDatabase.ContainsKey(itemID))
        {
            return inventoryDatabase[itemID];
        }
        return 0;
    }

    // --- Internal Server Logic ---

    private bool ModifyQuantityInternal(string itemID, int amount)
    {
        if (!inventoryDatabase.ContainsKey(itemID)) return false;

        int currentQuantity = inventoryDatabase[itemID];
        int newQuantity = currentQuantity + amount;

        if (newQuantity < 0) return false;

        inventoryDatabase[itemID] = newQuantity;
        
        OnItemQuantityChanged?.Invoke(itemID, newQuantity);
        UpdateItemClientRpc(itemID, newQuantity);

        return true;
    }

    // --- Networking RPCs ---

    [Rpc(SendTo.Server)]
    private void RequestItemChangeRpc(string itemID, int amount)
    {
        ModifyQuantityInternal(itemID, amount);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void UpdateItemClientRpc(string itemID, int newAmount)
    {
        if (IsServer) return; 

        inventoryDatabase[itemID] = newAmount;
        OnItemQuantityChanged?.Invoke(itemID, newAmount);
    }

    // FIXED: Added AllowTargetOverride = true to support RpcTarget.Single
    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
    private void SyncInventoryRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return; 

        InventorySaveData data = JsonUtility.FromJson<InventorySaveData>(json);
        if (data != null && data.Items != null)
        {
            inventoryDatabase.Clear();
            foreach (var item in data.Items)
            {
                inventoryDatabase[item.ID] = item.Count;
            }
            
            // Refresh UI
            foreach(var kvp in inventoryDatabase)
            {
                OnItemQuantityChanged?.Invoke(kvp.Key, kvp.Value);
            }
        }
    }

    // --- Serialization Helpers ---

    private string SerializeInventory()
    {
        InventorySaveData data = new InventorySaveData();
        data.Items = new List<InventoryEntry>();
        foreach (var kvp in inventoryDatabase)
        {
            data.Items.Add(new InventoryEntry { ID = kvp.Key, Count = kvp.Value });
        }
        return JsonUtility.ToJson(data);
    }
}

[System.Serializable]
public class InventorySaveData
{
    public List<InventoryEntry> Items;
}

[System.Serializable]
public struct InventoryEntry
{
    public string ID;
    public int Count;
}