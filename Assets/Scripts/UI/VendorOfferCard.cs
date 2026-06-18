using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

public class VendorOfferCardUI : MonoBehaviour
{
    public TextMeshProUGUI vendorNameText;
    public TextMeshProUGUI statsText;
    public Button signDealBtn;

    private VendorData _vendor;

    public void Setup(VendorData vendor)
    {
        _vendor = vendor;
        vendorNameText.text = $"<b>{vendor.Name}</b> ({vendor.Category} - {vendor.QualityTier})";
        statsText.text = $"Rel: {vendor.ReliabilityScore:F0}% | Spd: x{vendor.DeliverySpeedMultiplier:F1} | Price: x{vendor.PriceMultiplier:F1}\nQuality: {vendor.MinDurability:F0}-{vendor.MaxDurability:F0}";

        int activeInCat = VendorManager.Instance.activeDeals.Count(d => d.Category == vendor.Category);
        
        signDealBtn.onClick.RemoveAllListeners();
        signDealBtn.interactable = activeInCat < 2;
        
        if (signDealBtn.interactable)
        {
            signDealBtn.onClick.AddListener(() => VendorManager.Instance.SignDeal(_vendor.VendorID, _vendor.Category));
        }
    }
}