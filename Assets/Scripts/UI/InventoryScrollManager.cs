using UnityEngine;
using System.Collections.Generic;

public class InventoryScrollManager : BaseScrollManager<InventoryItemData>
{
    // You no longer need ANY detail references here!

    void Start()
    {
        GenerateMockData();

        PopulateList();
    }

    private void GenerateMockData()
    {
        activeItems.Clear();

        // Mock Item 1: Engine Part
        InventoryItemData item1 = ScriptableObject.CreateInstance<InventoryItemData>();
        item1.ItemID = "engine_v8";
        item1.DisplayName = "V8 Engine Block";
        item1.Category = ItemCategory.Part;
        item1.Cost = 2500.00f;
        // Icons are tricky to mock purely in code without loading resources, so we leave it null 
        activeItems.Add(item1);

        // Mock Item 2: Tire Part
        InventoryItemData item2 = ScriptableObject.CreateInstance<InventoryItemData>();
        item2.ItemID = "tire_all_season";
        item2.DisplayName = "All-Season Tire";
        item2.Category = ItemCategory.Part;
        item2.Cost = 150.50f;
        activeItems.Add(item2);

        // Mock Item 3: Transmission
        InventoryItemData item3 = ScriptableObject.CreateInstance<InventoryItemData>();
        item3.ItemID = "transmission_auto";
        item3.DisplayName = "Auto Transmission";
        item3.Category = ItemCategory.Part;
        item3.Cost = 1200.00f;
        activeItems.Add(item3);

        // Mock Item 4: Other Supplies
        InventoryItemData item4 = ScriptableObject.CreateInstance<InventoryItemData>();
        item4.ItemID = "cleaning_supplies";
        item4.DisplayName = "Interior Cleaning Kit";
        item4.Category = ItemCategory.Other;
        item4.Cost = 25.99f;
        activeItems.Add(item4);
    }

    protected override void SetupItemDisplay(GameObject instantiatedPrefab, InventoryItemData itemData)
    {
        // Get the specific inventory card script
        InventoryCardDisplay cardDisplay = instantiatedPrefab.GetComponent<InventoryCardDisplay>();

        if (cardDisplay != null)
        {
            // Just pass the data to the card, no callbacks needed
            cardDisplay.Setup(itemData);
        }
        else
        {
            Debug.LogWarning("InventoryCardDisplay script is missing on the Inventory Item Prefab!");
        }
    }
}