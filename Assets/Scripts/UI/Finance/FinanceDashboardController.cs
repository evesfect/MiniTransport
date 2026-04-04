using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Linq;
using XCharts.Runtime; // XCharts Library

public class FinanceDashboardController : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject dashboardPanel;
    
    [Header("Chart Elements")]
    public LineChart financeChart;
    public TMP_Dropdown categoryDropdown;
    public TextMeshProUGUI totalBalanceText;

    [Header("Approvals & Vendors (Containers)")]
    public Transform approvalListContent;
    public GameObject approvalItemPrefab; // A prefab with texts and buttons
    
    public Transform vendorListContent;
    public GameObject vendorItemPrefab;   // A prefab with texts and buttons

    private List<MockTransaction> _allTransactions;
    private bool _isOpen = false;

    private void Start()
    {
        dashboardPanel.SetActive(false);
        _allTransactions = MockDataGenerator.GenerateMockTransactions();
        
        SetupDropdown();
        PopulateMockApprovals();
        PopulateMockVendors(); // Placeholder for future merge
    }

    private void Update()
    {
        // Simple input check for 'F' key to toggle dashboard
        if (Keyboard.current.fKey.wasPressedThisFrame)
        {
            ToggleDashboard();
        }
    }

    public void ToggleDashboard()
    {
        _isOpen = !_isOpen;
        dashboardPanel.SetActive(_isOpen);

        if (_isOpen)
        {
            // Refresh chart when opened
            OnCategoryChanged(categoryDropdown.value);
            CalculateTotalBalance();
        }
    }

    private void SetupDropdown()
    {
        categoryDropdown.ClearOptions();
        List<string> options = new List<string> { "All (Net Cashflow)" };
        options.AddRange(System.Enum.GetNames(typeof(TransactionCategory)));
        categoryDropdown.AddOptions(options);

        categoryDropdown.onValueChanged.AddListener(OnCategoryChanged);
    }

    // --- XCHARTS LOGIC ---
    private void OnCategoryChanged(int index)
    {
        financeChart.ClearData();
        
        // Setup X-Axis
        var xAxis = financeChart.EnsureChartComponent<XAxis>();
        xAxis.splitNumber = 10;
        xAxis.type = Axis.AxisType.Category;

        // Filter data based on dropdown
        IEnumerable<MockTransaction> filteredData;
        if (index == 0) // "All"
        {
            financeChart.AddSerie<Line>("Net Cashflow");
            filteredData = _allTransactions;
        }
        else
        {
            TransactionCategory selectedCat = (TransactionCategory)(index - 1);
            financeChart.AddSerie<Line>(selectedCat.ToString());
            filteredData = _allTransactions.Where(t => t.Category == selectedCat);
        }

        // Group by Day and plot
        var groupedByDay = filteredData.GroupBy(t => t.GameDay).OrderBy(g => g.Key);

        foreach (var group in groupedByDay)
        {
            float dailyTotal = group.Sum(t => t.Amount);
            financeChart.AddXAxisData($"Day {group.Key}");
            financeChart.AddData(0, dailyTotal); // 0 is the index of the serie we just added
        }
    }

    private void CalculateTotalBalance()
    {
        // Starting fake balance
        float balance = 250000f + _allTransactions.Sum(t => t.Amount);
        totalBalanceText.text = $"Company Balance: ${balance:N0}";
    }

    // --- MOCK UI POPULATION ---
    private void PopulateMockApprovals()
    {
        var requests = MockDataGenerator.GenerateMockRequests();
        foreach (var req in requests)
        {
            GameObject item = Instantiate(approvalItemPrefab, approvalListContent);
            // Assuming your prefab has a text component
            var textComp = item.GetComponentInChildren<TextMeshProUGUI>();
            if (textComp != null)
            {
                textComp.text = $"<b>{req.Department}</b>\n{req.Description}\n<color=red>-${req.Cost:N0}</color>";
            }
        }
    }

    private void PopulateMockVendors()
    {
        // Simple mock to show where vendor relations will go
        string[] mockVendors = { "Apex Parts (Engine)", "Budget Bits (Chassis)" };
        foreach (var v in mockVendors)
        {
            GameObject item = Instantiate(vendorItemPrefab, vendorListContent);
            var textComp = item.GetComponentInChildren<TextMeshProUGUI>();
            if (textComp != null) textComp.text = v;
        }
    }
}