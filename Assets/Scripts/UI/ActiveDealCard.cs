using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

public class ActiveDealCardUI : MonoBehaviour
{
    [Header("Vendor Info")]
    public TextMeshProUGUI vendorInfoText;
    public TextMeshProUGUI feeText;
    public Button cancelDealBtn;

    [Header("Part Selection")]
    public TextMeshProUGUI selectedPartText;
    public Button prevPartBtn;
    public Button nextPartBtn;

    [Header("Ordering")]
    public Slider quantitySlider;
    public TextMeshProUGUI quantityText;
    public TextMeshProUGUI orderStatsText;
    public Button placeOrderBtn;

    private VendorData _vendor;
    private string[] _availableParts;
    private int _currentPartIndex = 0;

    public void Setup(VendorData vendor, ActiveDeal deal)
    {
        _vendor = vendor;
        _availableParts = VendorManager.CategoryParts[vendor.Category];

        vendorInfoText.text = $"<b>{vendor.Name}</b> (Lvl {vendor.LoyaltyLevel})\nBase Rel: {vendor.ReliabilityScore:F0}% | Spd: x{vendor.DeliverySpeedMultiplier:F1} | Price: x{vendor.PriceMultiplier:F1}\nQual Range: {vendor.MinDurability:F0}-{vendor.MaxDurability:F0}";

        int age = SimulationTimeManager.Instance.CurrentDay - deal.StartDay;
        bool isFree = age >= 7;
        feeText.text = isFree ? "<color=green>Free to Cancel</color>" : $"<color=yellow>Fee: ${VendorManager.Instance.contractCancellationFine}</color>";

        bool hasActiveOrder = VendorManager.Instance.activeOrders.Any(o => o.VendorID == vendor.VendorID);
        cancelDealBtn.interactable = !hasActiveOrder;
        cancelDealBtn.GetComponentInChildren<TextMeshProUGUI>().text = hasActiveOrder ? "Cannot Cancel" : "Cancel Contract";
        
        cancelDealBtn.onClick.RemoveAllListeners();
        cancelDealBtn.onClick.AddListener(() => VendorManager.Instance.CancelDeal(_vendor.VendorID));

        prevPartBtn.onClick.RemoveAllListeners();
        prevPartBtn.onClick.AddListener(SelectPrevPart);
        
        nextPartBtn.onClick.RemoveAllListeners();
        nextPartBtn.onClick.AddListener(SelectNextPart);

        quantitySlider.onValueChanged.RemoveAllListeners();
        quantitySlider.onValueChanged.AddListener(UpdateOrderUI);

        placeOrderBtn.onClick.RemoveAllListeners();
        placeOrderBtn.onClick.AddListener(PlaceOrder);

        UpdateOrderUI(quantitySlider.value);
    }

    private void SelectPrevPart()
    {
        _currentPartIndex--;
        if (_currentPartIndex < 0) _currentPartIndex = _availableParts.Length - 1;
        UpdateOrderUI(quantitySlider.value);
    }

    private void SelectNextPart()
    {
        _currentPartIndex = (_currentPartIndex + 1) % _availableParts.Length;
        UpdateOrderUI(quantitySlider.value);
    }

    private void UpdateOrderUI(float qtyValue)
    {
        int amount = Mathf.RoundToInt(qtyValue);
        quantityText.text = amount.ToString();
        selectedPartText.text = _availableParts[_currentPartIndex];

        var itemStats = VendorManager.Instance.GetItemStats(_vendor.VendorID, _availableParts[_currentPartIndex]);
        
        float estTime = VendorManager.Instance.baseDeliveryHours * itemStats.SpeedMultiplier;
        float delayProb = 100f - itemStats.Reliability;
        float exactPrice = 100f * itemStats.PriceMultiplier * amount;

        orderStatsText.text = $"Est: {estTime:F1}h | Risk: {delayProb:F0}%\nPrice: ${exactPrice:F0} | Quality: {itemStats.Durability:F0}";
    }

    private void PlaceOrder()
    {
        int amount = Mathf.RoundToInt(quantitySlider.value);
        VendorManager.Instance.PlaceOrder(_vendor.VendorID, _availableParts[_currentPartIndex], amount);
    }
}