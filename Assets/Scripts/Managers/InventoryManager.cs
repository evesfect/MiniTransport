using UnityEngine;
using System.Collections.Generic;
using System;
using Unity.Netcode;
using System.Linq;
using System.IO;

public class InventoryManager : NetworkBehaviour
{
    public static InventoryManager Instance { get; private set; }

    private Dictionary<string, List<PartCondition>> inventoryDatabase = new Dictionary<string, List<PartCondition>>();

    [HideInInspector]
    public InventoryItemData[] allAvailableItems;

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

            
            allAvailableItems = Resources.LoadAll<InventoryItemData>("InventoryItems");
            Debug.Log($"[InventoryManager] Auto-loaded {allAvailableItems.Length} item definitions from Resources.");
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
        else { inventoryDatabase.Clear(); }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnApplicationQuit() { if(IsServer) SaveInventory(); }

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
        // POINT 3: Deliberately left empty. 
        // We no longer pre-fill the database with dummy empty lists from allAvailableItems.
        // Lists are naturally created only when a part actually arrives.
    }

    [ContextMenu("Save Inventory")]
    public void SaveInventory() { File.WriteAllText(SavePath, SerializeInventory()); }

    [ContextMenu("Load Inventory")]
    public void LoadInventory()
    {
        InitializeInventory(); 
        if (File.Exists(SavePath)) ApplyJsonToDatabase(File.ReadAllText(SavePath));
    }

    public void AddPartWithDurability(string itemID, float durability)
    {
        if (IsServer) ModifyQuantityInternal(itemID, durability, true);
        else RequestPartAddRpc(itemID, durability);
    }

    public void IncreaseItemQuantity(string itemID, int amount)
    {
        if (amount <= 0) return;
        for (int i = 0; i < amount; i++) AddPartWithDurability(itemID, 100f);
    }

    public bool DecreaseItemQuantity(string itemID, int amount)
    {
        if (amount <= 0) return false;
        if (!inventoryDatabase.ContainsKey(itemID) || inventoryDatabase[itemID].Count < amount) return false;

        if (IsServer)
        {
            for (int i = 0; i < amount; i++) inventoryDatabase[itemID].RemoveAt(0);
            
            OnItemQuantityChanged?.Invoke(itemID, inventoryDatabase[itemID].Count);
            UpdateItemClientRpc(itemID, SerializeInventory());
            
            // POINT 3: Real-time saving
            SaveInventory();
            return true;
        }
        else
        {
            RequestPartRemoveRpc(itemID, amount);
            return true; 
        }
    }

    public int GetItemQuantity(string itemID)
    {
        return inventoryDatabase.ContainsKey(itemID) ? inventoryDatabase[itemID].Count : 0;
    }

    public bool TryConsumeItem(string itemID, out float consumedDurability)
    {
        consumedDurability = 0f;

        // Check if we have the item in stock
        if (!inventoryDatabase.ContainsKey(itemID) || inventoryDatabase[itemID].Count == 0)
            return false;

        if (IsServer)
        {
            // Grab the durability of the first part in the stack (First In, First Out)
            consumedDurability = inventoryDatabase[itemID][0].MaxDurability;

            // Remove it from the inventory
            inventoryDatabase[itemID].RemoveAt(0);

            // Sync and Save
            OnItemQuantityChanged?.Invoke(itemID, inventoryDatabase[itemID].Count);
            UpdateItemClientRpc(itemID, SerializeInventory());
            SaveInventory();

            return true;
        }

        return false;
    }

    private void ModifyQuantityInternal(string itemID, float durability, bool isAdd)
    {
        if (!inventoryDatabase.ContainsKey(itemID)) 
        {
            inventoryDatabase.Add(itemID, new List<PartCondition>());
        }

        if (isAdd)
        {
            inventoryDatabase[itemID].Add(new PartCondition { CurrentDurability = durability, MaxDurability = durability });
        }

        OnItemQuantityChanged?.Invoke(itemID, inventoryDatabase[itemID].Count);
        UpdateItemClientRpc(itemID, SerializeInventory());
        
        // POINT 3: Real-time saving
        SaveInventory();
    }

    [Rpc(SendTo.Server)] private void RequestPartAddRpc(string itemID, float durability) { ModifyQuantityInternal(itemID, durability, true); }
    [Rpc(SendTo.Server)] private void RequestPartRemoveRpc(string itemID, int amount) { DecreaseItemQuantity(itemID, amount); }

    [Rpc(SendTo.ClientsAndHost)]
    private void UpdateItemClientRpc(string itemID, string fullJson)
    {
        if (IsServer) return; 
        ApplyJsonToDatabase(fullJson);
        OnItemQuantityChanged?.Invoke(itemID, inventoryDatabase.ContainsKey(itemID) ? inventoryDatabase[itemID].Count : 0);
    }

    [Rpc(SendTo.ClientsAndHost, AllowTargetOverride = true)]
    private void SyncInventoryRpc(string json, RpcParams rpcParams = default)
    {
        if (IsServer) return; 
        ApplyJsonToDatabase(json);
        foreach(var kvp in inventoryDatabase) OnItemQuantityChanged?.Invoke(kvp.Key, kvp.Value.Count);
    }

    private void ApplyJsonToDatabase(string json)
    {
        InventorySaveData data = JsonUtility.FromJson<InventorySaveData>(json);
        if (data != null && data.Items != null)
        {
            inventoryDatabase.Clear();
            foreach (var item in data.Items) inventoryDatabase[item.ID] = item.Conditions ?? new List<PartCondition>();
        }
    }

    private string SerializeInventory()
    {
        InventorySaveData data = new InventorySaveData { Items = new List<InventoryEntry>() };
        foreach (var kvp in inventoryDatabase) data.Items.Add(new InventoryEntry { ID = kvp.Key, Conditions = kvp.Value });
        return JsonUtility.ToJson(data);
    }
}

[System.Serializable]
public class PartCondition
{
    public float CurrentDurability;
    public float MaxDurability;
}

[System.Serializable]
public class InventorySaveData { public List<InventoryEntry> Items; }

[System.Serializable]
public struct InventoryEntry { public string ID; public List<PartCondition> Conditions; }