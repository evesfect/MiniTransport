using UnityEngine;
using System.Linq;
using System;
using System.Collections.Generic;

public class VendorDebugger : MonoBehaviour
{
    private Rect _windowRect = new Rect(380, 20, 500, 700); 
    private bool _isCollapsed = false;
    private Vector2 _scrollDeals, _scrollMarket, _scrollOrders;

    private Dictionary<string, int> _partSelections = new Dictionary<string, int>();
    private Dictionary<string, float> _orderAmounts = new Dictionary<string, float>(); // Added to track slider quantities

    private void OnGUI()
    {
        _windowRect.height = _isCollapsed ? 60f : 700f;
        _windowRect = GUI.Window(998, _windowRect, DrawWindow, "Vendor Relations");
    }

    private void DrawWindow(int id)
    {
        if (VendorManager.Instance == null || SimulationTimeManager.Instance == null) return;

        GUILayout.BeginHorizontal(GUI.skin.box);
        if (GUILayout.Button(_isCollapsed ? "+" : "-", GUILayout.Width(30))) _isCollapsed = !_isCollapsed;
        GUILayout.Label($"Finance & Vendors (Day {SimulationTimeManager.Instance.CurrentDay})");
        GUILayout.EndHorizontal();

        if (!_isCollapsed)
        {
            DrawActiveDeals();
            GUILayout.Space(5);
            DrawActiveOrders();
            GUILayout.Space(5);
            DrawMarket();
        }
        GUI.DragWindow();
    }

    private void DrawActiveDeals()
    {
        GUILayout.Label("Active Deals & Ordering (Max 2 per category)", GUI.skin.box);
        _scrollDeals = GUILayout.BeginScrollView(_scrollDeals, GUILayout.Height(250));

        foreach (BusPartCategory cat in Enum.GetValues(typeof(BusPartCategory)))
        {
            if (cat == BusPartCategory.None) continue;
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(cat.ToString(), GUILayout.Width(80));

            var dealsForCat = VendorManager.Instance.activeDeals.Where(d => d.Category == cat).ToList();
            
            for(int i=0; i<2; i++)
            {
                if (i < dealsForCat.Count)
                {
                    var deal = dealsForCat[i];
                    var vendor = VendorManager.Instance.availableVendors.FirstOrDefault(v => v.VendorID == deal.VendorID);
                    if (vendor == null) continue;

                    GUILayout.BeginVertical("box", GUILayout.Width(190));
                    
                    GUILayout.Label($"<b>{vendor.Name}</b> (Lvl {vendor.LoyaltyLevel})");
                    GUILayout.Label($"Base Rel: {vendor.ReliabilityScore:F0}% | Spd: x{vendor.DeliverySpeedMultiplier:F1} | Price: x{vendor.PriceMultiplier:F1}");
                    GUILayout.Label($"Quality Range: {vendor.MinDurability:F0}-{vendor.MaxDurability:F0}");
                    
                    int age = SimulationTimeManager.Instance.CurrentDay - deal.StartDay;
                    GUI.color = age >= 7 ? Color.green : Color.yellow;
                    GUILayout.Label(age >= 7 ? "Free to Cancel" : $"Fee: ${VendorManager.Instance.contractCancellationFine}");
                    GUI.color = Color.white;

                    bool hasActiveOrder = VendorManager.Instance.activeOrders.Any(o => o.VendorID == deal.VendorID);
                    GUI.enabled = !hasActiveOrder;
                    if (GUILayout.Button(hasActiveOrder ? "Cannot Cancel (Active Order)" : "Cancel Contract")) 
                    {
                        VendorManager.Instance.CancelDeal(deal.VendorID);
                    }
                    GUI.enabled = true; 
                    
                    GUILayout.Space(5);
                    
                    // --- Dynamic Part Selector ---
                    string[] parts = VendorManager.CategoryParts[cat];
                    if (!_partSelections.ContainsKey(deal.VendorID)) _partSelections[deal.VendorID] = 0;
                    int selectedIndex = _partSelections[deal.VendorID];

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("<", GUILayout.Width(25))) {
                        selectedIndex--; 
                        if (selectedIndex < 0) selectedIndex = parts.Length - 1;
                        _partSelections[deal.VendorID] = selectedIndex;
                    }
                    GUILayout.Label(parts[selectedIndex], new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter }, GUILayout.Width(80));
                    if (GUILayout.Button(">", GUILayout.Width(25))) {
                        selectedIndex = (selectedIndex + 1) % parts.Length;
                        _partSelections[deal.VendorID] = selectedIndex;
                    }
                    GUILayout.EndHorizontal();

                    // --- Quantity Selector ---
                    if (!_orderAmounts.ContainsKey(deal.VendorID)) _orderAmounts[deal.VendorID] = 1f;
                    
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Qty:", GUILayout.Width(25));
                    _orderAmounts[deal.VendorID] = GUILayout.HorizontalSlider(_orderAmounts[deal.VendorID], 1f, 50f);
                    int orderAmount = Mathf.RoundToInt(_orderAmounts[deal.VendorID]);
                    GUILayout.Label(orderAmount.ToString(), GUILayout.Width(20));
                    GUILayout.EndHorizontal();

                    var itemStats = VendorManager.Instance.GetItemStats(vendor.VendorID, parts[selectedIndex]);
                    float estTime = VendorManager.Instance.baseDeliveryHours * itemStats.SpeedMultiplier;
                    float delayProb = 100f - itemStats.Reliability;
                    
                    // Multiply exact price by the quantity slider
                    float exactPrice = 100f * itemStats.PriceMultiplier * orderAmount; 
                    
                    GUILayout.Label($"Est: {estTime:F1}h | Risk: {delayProb:F0}%", new GUIStyle(GUI.skin.label) { fontSize = 11 });
                    GUILayout.Label($"Price: ${exactPrice:F0} | Quality: {itemStats.Durability:F0}", new GUIStyle(GUI.skin.label) { fontSize = 11 });

                    if (GUILayout.Button("Place Order")) 
                    {
                        VendorManager.Instance.PlaceOrder(vendor.VendorID, parts[selectedIndex], orderAmount);
                    }

                    GUILayout.EndVertical();
                }
                else
                {
                    GUILayout.BeginVertical("box", GUILayout.Width(190));
                    GUILayout.Label("\n[ Empty Slot ]\n");
                    GUILayout.EndVertical();
                }
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }

    private void DrawActiveOrders()
    {
        GUILayout.Label("Pending Deliveries", GUI.skin.box);
        _scrollOrders = GUILayout.BeginScrollView(_scrollOrders, GUILayout.Height(100));
        
        float currentAbsHour = SimulationTimeManager.Instance.CurrentDay * 24f + SimulationTimeManager.Instance.CurrentTimeOfDay;

        foreach(var order in VendorManager.Instance.activeOrders)
        {
            GUILayout.BeginHorizontal("box");
            
            // This will naturally display "Tire4-13" if it was a bulk order
            GUILayout.Label($"Item: {order.ItemID}", GUILayout.Width(150)); 
            
            if (currentAbsHour < order.ExpectedArrivalHour)
            {
                float hoursLeft = Mathf.Max(0f, order.ExpectedArrivalHour - currentAbsHour);
                GUILayout.Label($"Est: {hoursLeft:F1}h");
            }
            else
            {
                if (order.IsDelayed)
                {
                    float delayLeft = Mathf.Max(0f, order.ActualArrivalHour - currentAbsHour);
                    GUILayout.Label($"<color=red>DELAYED! {delayLeft:F1}h left</color>");
                }
                else
                {
                    GUILayout.Label("Arriving...");
                }
            }

            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }

    private void DrawMarket()
    {
        GUILayout.Label("Weekly Market Offers", GUI.skin.box);
        _scrollMarket = GUILayout.BeginScrollView(_scrollMarket, GUILayout.Height(150));

        foreach (var vendor in VendorManager.Instance.availableVendors)
        {
            if (VendorManager.Instance.activeDeals.Any(d => d.VendorID == vendor.VendorID)) continue;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>{vendor.Name}</b> ({vendor.Category} - {vendor.QualityTier})");
            GUILayout.Label($"Rel: {vendor.ReliabilityScore:F0}% | Spd: x{vendor.DeliverySpeedMultiplier:F1} | Price: x{vendor.PriceMultiplier:F1}");
            GUILayout.Label($"Quality Range: {vendor.MinDurability:F0}-{vendor.MaxDurability:F0}");
            
            int activeInCat = VendorManager.Instance.activeDeals.Count(d => d.Category == vendor.Category);
            if (activeInCat < 2)
            {
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button("Sign Deal")) VendorManager.Instance.SignDeal(vendor.VendorID, vendor.Category);
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUILayout.Label("<color=red>Max deals for this category reached.</color>");
            }
            GUILayout.EndVertical();
        }
        GUILayout.EndScrollView();
    }
}