using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VendorPanelUI : MonoBehaviour
{
    [Header("Market (Left Side)")]
    public Transform marketContainer;
    public VendorOfferCardUI offerCardPrefab;

    [Header("Active Deals (Right Top)")]
    public TextMeshProUGUI categoryTitleText;
    public Button prevCategoryBtn;
    public Button nextCategoryBtn;
    public Transform activeDealsContainer;
    public ActiveDealCardUI activeDealCardPrefab;

    [Header("Pending Orders (Right Bottom)")]
    public Transform ordersContainer;
    public PendingOrderCardUI pendingOrderCardPrefab;

    private List<BusPartCategory> _categories;
    private int _currentCategoryIndex = 0;

    private void OnEnable()
    {
        // Extract categories ignoring 'None'
        _categories = Enum.GetValues(typeof(BusPartCategory)).Cast<BusPartCategory>().Where(c => c != BusPartCategory.None).ToList();
        
        prevCategoryBtn.onClick.AddListener(PrevCategory);
        nextCategoryBtn.onClick.AddListener(NextCategory);

        if (VendorManager.Instance != null)
        {
            VendorManager.Instance.OnVendorDataUpdated += RefreshAll;
            RefreshAll();
        }
    }

    private void OnDisable()
    {
        prevCategoryBtn.onClick.RemoveAllListeners();
        nextCategoryBtn.onClick.RemoveAllListeners();
        if (VendorManager.Instance != null)
        {
            VendorManager.Instance.OnVendorDataUpdated -= RefreshAll;
        }
    }

    private void PrevCategory()
    {
        _currentCategoryIndex--;
        if (_currentCategoryIndex < 0) _currentCategoryIndex = _categories.Count - 1;
        RefreshActiveDeals();
    }

    private void NextCategory()
    {
        _currentCategoryIndex = (_currentCategoryIndex + 1) % _categories.Count;
        RefreshActiveDeals();
    }

    public void RefreshAll()
    {
        RefreshMarket();
        RefreshActiveDeals();
        RefreshPendingOrders();
    }

    private void RefreshMarket()
    {
        foreach (Transform child in marketContainer) Destroy(child.gameObject);

        foreach (var vendor in VendorManager.Instance.availableVendors)
        {
            // Only show vendors we don't have a deal with
            if (VendorManager.Instance.activeDeals.Any(d => d.VendorID == vendor.VendorID)) continue;

            var card = Instantiate(offerCardPrefab, marketContainer);
            card.Setup(vendor);
        }
    }

    private void RefreshActiveDeals()
    {
        foreach (Transform child in activeDealsContainer) Destroy(child.gameObject);

        BusPartCategory currentCat = _categories[_currentCategoryIndex];
        categoryTitleText.text = $"{currentCat} Vendors";

        var dealsInCat = VendorManager.Instance.activeDeals.Where(d => d.Category == currentCat).ToList();

        foreach (var deal in dealsInCat)
        {
            var vendor = VendorManager.Instance.availableVendors.FirstOrDefault(v => v.VendorID == deal.VendorID);
            if (vendor != null)
            {
                var card = Instantiate(activeDealCardPrefab, activeDealsContainer);
                card.Setup(vendor, deal);
            }
        }
    }

    private void RefreshPendingOrders()
    {
        foreach (Transform child in ordersContainer) Destroy(child.gameObject);

        foreach (var order in VendorManager.Instance.activeOrders)
        {
            var card = Instantiate(pendingOrderCardPrefab, ordersContainer);
            card.Setup(order);
        }
    }
}