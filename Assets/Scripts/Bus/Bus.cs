using UnityEngine;

public class Bus : MonoBehaviour
{
    [Header("Bus Identification")]

    public string BusID;
    public string BusModelName;

    [Header("Bus Attributes")]

    public int MaxCapacity;

    public BusPartStatus[] BusParts;

    public bool IsInService { get; private set; } = false;

    void Start()
    {
        if(BusParts == null || BusParts.Length == 0)
        {
            //Initilization logic with bus parts
        }    
    }

    /// <summary>
    /// Reduces the health of a specific part by a given amount.
    /// Used for damage, wear-and-tear simulation, etc.
    /// </summary>
    /// <param name="partName">The display name of the part to damage.</param>
    /// <param name="damageAmount">The amount to reduce health by (e.g., 0.1 for 10% wear).</param>
    /// 
    public void DamagePart(string partName, float damageAmount)
    {
        for (int i = 0; i < BusParts.Length; i++)
        {
            // Use the part's DisplayName for lookup
            if (BusParts[i].PartReference.DisplayName == partName)
            {
                // Ensure we don't go below 0 health
                BusParts[i].Health = Mathf.Max(0f, BusParts[i].Health - damageAmount);
                Debug.Log($"Part {partName} on Bus {BusID} damaged. New Health: {BusParts[i].Health:P0}");
                return;
            }
        }
        Debug.LogWarning($"Part named '{partName}' not found on Bus {BusID}.");
    }

    /// <summary>
    /// Repairs a part by setting its health to maximum (1.0).
    /// Typically called after a successful InventoryManager.DecreaseItemQuantity call.
    /// </summary>
    /// <param name="partToRepairID">The ItemID of the part being replaced/repaired.</param>
    public void RepairPart(string partToRepairID)
    {
        for (int i = 0; i < BusParts.Length; i++)
        {
            if (BusParts[i].PartReference.ItemID == partToRepairID)
            {
                BusParts[i].Health = 1.0f; // Restore to full health
                Debug.Log($"Part {BusParts[i].PartReference.DisplayName} on Bus {BusID} repaired to 100%.");
                return;
            }
        }
        Debug.LogWarning($"Part with ID '{partToRepairID}' not found for repair on Bus {BusID}.");
    }
}

