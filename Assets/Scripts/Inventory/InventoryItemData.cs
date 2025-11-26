using UnityEngine;
using System;
using UnityEngine.Rendering;

[CreateAssetMenu(fileName ="NewInventoryItem", menuName = "Inventory/Item Data")]

public class InventoryItemData : ScriptableObject
{
    [Header("Item Identification")]
    [Tooltip("Unique ID for this item type")]
    public string ItemID;

    [Header("Item Information")]
    public string DisplayName;
    public string Description;

    //Enum to categorize items when needed
    public ItemCategory Category;

    public float Cost;
    
}

public enum ItemCategory
{
    //Update as needed
    Part,
    Other
}
