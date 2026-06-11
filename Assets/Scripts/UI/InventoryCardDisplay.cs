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

    // Cache the ID so the card remembers who it is when the megaphone shouts
    private string _currentItemId;

    public void Setup(InventoryItemData item)
    {
        _currentItemId = item.ItemID;

        // 1. Set the static data from your ScriptableObject
        if (nameText != null) nameText.text = item.DisplayName;
        if (idText != null) idText.text = $"ID: {item.ItemID}";
        if (categoryText != null) categoryText.text = $"Category: {item.Category}";

        if (itemIcon != null && item.icon != null)
        {
            itemIcon.sprite = item.icon;
        }

        // 2. Fetch the initial live stock number from your InventoryManager
        int currentStock = 0;
        if (InventoryManager.Instance != null)
        {
            currentStock = InventoryManager.Instance.GetItemQuantity(_currentItemId);
        }

        // 3. Update the visual text and colors
        UpdateStockVisuals(currentStock);
    }

    // --- EVENT DRIVEN UI LOGIC ---

    private void OnEnable()
    {
        // Subscribe to the broadcaster
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnItemQuantityChanged += OnStockChanged;
        }
    }

    private void OnDisable()
    {
        // Always unsubscribe to prevent memory leaks when the UI is destroyed or hidden!
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnItemQuantityChanged -= OnStockChanged;
        }
    }

    // This runs automatically whenever InventoryManager adds or removes ANY item
    private void OnStockChanged(string changedItemID, int newQuantity)
    {
        // Check if the item that changed is THIS card's item
        if (!string.IsNullOrEmpty(_currentItemId) && _currentItemId == changedItemID)
        {
            UpdateStockVisuals(newQuantity);
        }
    }

    // Helper method to keep our logic clean
    private void UpdateStockVisuals(int quantity)
    {
        if (stockText != null)
        {
            stockText.text = $"Stock: {quantity}";
        }

        if (cardBackgroundImage != null)
        {
            if (quantity < lowStockThreshold)
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