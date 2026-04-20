using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryCardDisplay : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text nameText;
    public TMP_Text idText;
    public TMP_Text categoryText;
    public TMP_Text stockText;
    public Image itemIcon;

    [Header("Stock Colors")]
    [Tooltip("The Image component that makes up the background of the card")]
    public Image cardBackgroundImage;

    public Color normalColor = Color.white; // Default color
    public Color lowStockColor = new Color(1f, 0.6f, 0.6f); // Soft Red

    [Tooltip("If stock falls below this number, the card turns red")]
    public int lowStockThreshold = 10;

    public void Setup(InventoryItemData item)
    {
        // 1. Set the static data from your ScriptableObject
        if (nameText != null) nameText.text = item.DisplayName;
        if (idText != null) idText.text = $"ID: {item.ItemID}";
        if (categoryText != null) categoryText.text = $"Category: {item.Category}";

        if (itemIcon != null && item.icon != null)
        {
            itemIcon.sprite = item.icon;
        }

        // 2. DEMO OVERRIDE: Injecting fake stock numbers directly into the UI
        if (stockText != null)
        {
            int fakeStock = 0;

            // Give specific demo items static fake numbers so they don't change every time you open the menu
            switch (item.ItemID)
            {
                case "engine_v8":
                    fakeStock = 5;
                    break;
                case "tire_all_season":
                    fakeStock = 24;
                    break;
                case "transmission_auto":
                    fakeStock = 2;
                    break;
                case "cleaning_supplies":
                    fakeStock = 100;
                    break;
                default:
                    // Any other random items you add to the demo will get a random stock number
                    fakeStock = UnityEngine.Random.Range(1, 15);
                    break;
            }

            stockText.text = $"Stock: {fakeStock}";

            if (cardBackgroundImage != null)
            {
                if (fakeStock < lowStockThreshold)
                {
                    cardBackgroundImage.color = lowStockColor;
                }
                else
                {
                    cardBackgroundImage.color = normalColor;
                }
            }
        }
    }
}

//Actual function down below

/*public void Setup(InventoryItemData item)
{
    // 1. Set the static data from your ScriptableObject
    if (nameText != null) nameText.text = item.DisplayName;
    if (idText != null) idText.text = $"ID: {item.ItemID}";
    if (categoryText != null) categoryText.text = $"Category: {item.Category}";


    if (itemIcon != null && item.icon != null)
    {
        itemIcon.sprite = item.icon;
    }

    // 2. Fetch the live stock number from your InventoryManager
    if (stockText != null)
    {
        int currentStock = 0;
        if (InventoryManager.Instance != null)
        {
            currentStock = InventoryManager.Instance.GetItemQuantity(item.ItemID);
        }
        stockText.text = $"Stock: {currentStock}";
    }
}
}*/