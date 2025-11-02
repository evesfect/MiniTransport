using UnityEngine;
using System.Collections.Generic;
using System;

public class InventoryManager : MonoBehaviour
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

    void Awake()
    {
        // Enforce Single Instance if DB
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        InitializeInventory();
    }

    /// <summary>
    /// Initializes the inventory with all defined item types.
    /// </summary>
    private void InitializeInventory()
    {
        // Pre-populate the database with all known item types with a starting quantity of 0
        if (allAvailableItems != null)
        {
            foreach (var item in allAvailableItems)
            {
                if (!inventoryDatabase.ContainsKey(item.ItemID))
                {
                    inventoryDatabase.Add(item.ItemID, 0);
                    Debug.Log($"Inventory initialized with item: {item.DisplayName} ({item.ItemID})");
                }
            }
        }
    }

    /// <summary>
    /// Increases the quantity of a specific item in the inventory.
    /// </summary>
    /// <param name="itemID">The unique ID of the item.</param>
    /// <param name="amount">The quantity to add.</param>
    public void IncreaseItemQuantity(string itemID, int amount)
    {
        if (amount <= 0) return;

        if (inventoryDatabase.ContainsKey(itemID))
        {
            inventoryDatabase[itemID] += amount;
            Debug.Log($"Added {amount} of {itemID}. New quantity: {inventoryDatabase[itemID]}");
            OnItemQuantityChanged?.Invoke(itemID, inventoryDatabase[itemID]);
        }
        else
        {
            Debug.LogError($"Item ID '{itemID}' not found in the database. Cannot add quantity.");
        }
    }

    /// <summary>
    /// Decreases the quantity of a specific item in the inventory.
    /// </summary>
    /// <param name="itemID">The unique ID of the item.</param>
    /// <param name="amount">The quantity to remove (must be positive).</param>
    /// <returns>True if the quantity was successfully decreased, false otherwise (e.g., insufficient stock).</returns>
    public bool DecreaseItemQuantity(string itemID, int amount)
    {
        if (amount <= 0) return false;

        if (inventoryDatabase.ContainsKey(itemID))
        {
            int currentQuantity = inventoryDatabase[itemID];

            if (currentQuantity >= amount)
            {
                inventoryDatabase[itemID] -= amount;
                Debug.Log($"Removed {amount} of {itemID}. New quantity: {inventoryDatabase[itemID]}");
                OnItemQuantityChanged?.Invoke(itemID, inventoryDatabase[itemID]);
                return true;
            }
            else
            {
                Debug.LogWarning($"Insufficient stock for item ID '{itemID}'.");
                return false;
            }
        }
        else
        {
            Debug.LogError($"Item ID '{itemID}' not found in the database.");
            return false;
        }
    }

    /// <summary>
    /// Gets the current quantity of a specific item.
    /// </summary>
    /// <param name="itemID">The unique ID of the item.</param>
    /// <returns>The current quantity, or 0 if the item ID is not found.</returns>
    public int GetItemQuantity(string itemID)
    {
        if (inventoryDatabase.ContainsKey(itemID))
        {
            return inventoryDatabase[itemID];
        }
        return 0;
    }


}
