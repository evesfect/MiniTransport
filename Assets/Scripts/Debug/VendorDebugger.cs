using UnityEngine;
using System.Linq;
using System;

public class VendorDebugger : MonoBehaviour
{
    private Rect _windowRect = new Rect(380, 20, 400, 500); // Offset to right of Employee Debugger
    private bool _isCollapsed = false;
    private Vector2 _scrollPos;

    // Simulation Test
    private BusPartCategory _testOrderCategory = BusPartCategory.Engine;

    private const float COLLAPSED_HEIGHT = 60f;
    private const float EXPANDED_HEIGHT = 500f;

    private void OnGUI()
    {
        _windowRect.height = _isCollapsed ? COLLAPSED_HEIGHT : EXPANDED_HEIGHT;
        _windowRect = GUI.Window(998, _windowRect, DrawWindow, "Vendor Relations");
    }

    private void DrawWindow(int id)
    {
        if (VendorManager.Instance == null)
        {
            GUILayout.Label("Waiting for VendorManager...");
            GUI.DragWindow();
            return;
        }

        GUILayout.BeginVertical();
        
        // Header
        GUILayout.BeginHorizontal(GUI.skin.box);
        if (GUILayout.Button(_isCollapsed ? "+" : "-", GUILayout.Width(30))) _isCollapsed = !_isCollapsed;
        GUILayout.Label("Finance Manager: Vendors");
        GUILayout.EndHorizontal();

        if (!_isCollapsed)
        {
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            
            DrawActiveDeals();
            GUILayout.Space(10);
            DrawVendorList();
            GUILayout.Space(10);
            DrawSimulationTest();

            GUILayout.EndScrollView();
        }

        GUILayout.EndVertical();
        GUI.DragWindow();
    }

    private void DrawActiveDeals()
    {
        GUILayout.Label("Active Deals", GUI.skin.box);
        
        // Loop through all categories to show current status
        foreach (BusPartCategory cat in Enum.GetValues(typeof(BusPartCategory)))
        {
            if (cat == BusPartCategory.None) continue;

            GUILayout.BeginHorizontal();
            GUILayout.Label(cat.ToString(), GUILayout.Width(80));

            var deal = VendorManager.Instance.activeDeals.FirstOrDefault(d => d.Category == cat);
            if (deal != null)
            {
                var vendor = VendorManager.Instance.allVendors.FirstOrDefault(v => v.VendorID == deal.VendorID);
                GUI.color = Color.cyan;
                GUILayout.Label(vendor != null ? vendor.Name : deal.VendorID, GUILayout.Width(150));
                GUI.color = Color.white;
                
                if (GUILayout.Button("X", GUILayout.Width(30)))
                {
                    VendorManager.Instance.CancelDeal(cat);
                }
            }
            else
            {
                GUI.color = Color.gray;
                GUILayout.Label("---", GUILayout.Width(150));
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();
        }
    }

    private void DrawVendorList()
    {
        GUILayout.Label("Available Vendors", GUI.skin.box);

        foreach (var vendor in VendorManager.Instance.allVendors)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>{vendor.Name}</b> (Lvl {vendor.LoyaltyLevel})");
            GUILayout.Label($"Reliability: {vendor.ReliabilityScore:F1}% | Price: x{vendor.PriceMultiplier:F2}");
            GUILayout.Label($"<i>{vendor.Description}</i>");

            // Deal Buttons
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sign Deal:", GUILayout.Width(70));
            foreach (BusPartCategory cat in Enum.GetValues(typeof(BusPartCategory)))
            {
                if (cat == BusPartCategory.None) continue;
                
                // Check if we already have a deal with THIS vendor for THIS category
                bool hasThisDeal = VendorManager.Instance.activeDeals.Any(d => d.Category == cat && d.VendorID == vendor.VendorID);
                
                if (hasThisDeal)
                {
                    GUI.backgroundColor = Color.green;
                    GUILayout.Button(cat.ToString().Substring(0, 3)); // visual only
                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    if (GUILayout.Button(cat.ToString().Substring(0, 3)))
                    {
                        VendorManager.Instance.SignDeal(vendor.VendorID, cat);
                    }
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }

    private void DrawSimulationTest()
    {
        GUILayout.Label("Simulation / Test", GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Order Part:");
        // Simple cycle button
        if (GUILayout.Button(_testOrderCategory.ToString()))
        {
            _testOrderCategory++;
            if (_testOrderCategory > BusPartCategory.Electronics) _testOrderCategory = BusPartCategory.Engine;
        }

        if (GUILayout.Button("Simulate Order"))
        {
            bool result = VendorManager.Instance.ProcessOrder(_testOrderCategory);
            Debug.Log(result ? "Order Arrived ON TIME" : "Order DELAYED");
        }
        GUILayout.EndHorizontal();
    }
}