using UnityEngine;
using System;

[Serializable]
public struct BusPartStatus
{
    
    // Reference the InventoryItemData to know what type of item is needed for repair
    public InventoryItemData PartReference;

    [Header("Current Status")]
    
    [Range(0f, 1f)]
    public float Health;

    // Maximum health reduction before the part requires immediate maintenance
    [Range(0.01f, 0.99f)]
    public float CriticalThreshold;

    public bool NeedsRepair => Health <= CriticalThreshold;

    public float MissingHealth => 1f - Health;
}