using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using XCharts.Runtime;

public class FinanceDashboardUI : MonoBehaviour
{
    [Header("Scroll View Settings")]
    public Transform transactionContentContainer;
    public TransactionCardUI transactionPrefab;
    public int maxScrollItems = 15;

    [Header("XCharts Settings")]
    public LineChart financeChart;
    public TMP_Dropdown categoryDropdown;
    public int maxChartItems = 30;

    [Header("Performance")]
    [Tooltip("How often (in seconds) the dashboard is allowed to refresh.")]
    public float refreshCooldown = 120f; // 2 minutes

    private List<string> _dropdownOptions = new List<string>();
    
    // Throttling variables
    private bool _isDirty = false;
    private float _refreshTimer = 0f;

    private void OnEnable()
    {
        SetupDropdown();

        if (CompanyManager.Instance != null)
        {
            CompanyManager.Instance.OnLedgerUpdated += MarkDirty;
            
            // Instantly reset the cooldowns when the panel is opened
            _isDirty = false;
            _refreshTimer = 0f;
            
            // Force an immediate UI draw with the freshest data
            ForceRefresh(); 
        }
    }

    private void OnDisable()
    {
        if (CompanyManager.Instance != null)
        {
            CompanyManager.Instance.OnLedgerUpdated -= MarkDirty;
        }
    }

    private void SetupDropdown()
    {
        categoryDropdown.ClearOptions();
        _dropdownOptions.Clear();

        _dropdownOptions.Add("All");

        foreach (string cat in Enum.GetNames(typeof(TransactionCategory)))
        {
            _dropdownOptions.Add(cat);
        }

        categoryDropdown.AddOptions(_dropdownOptions);
        
        categoryDropdown.onValueChanged.RemoveAllListeners();
        categoryDropdown.onValueChanged.AddListener(delegate { ForceRefresh(); });
    }

    private void MarkDirty()
    {
        _isDirty = true;
    }

    private void Update()
    {
        if (_isDirty)
        {
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= refreshCooldown)
            {
                ForceRefresh();
                _isDirty = false;
                _refreshTimer = 0f;
            }
        }
    }

    private void ForceRefresh()
    {
        RefreshScrollView();
        RefreshChart();
    }

    private void RefreshScrollView()
    {
        foreach (Transform child in transactionContentContainer)
        {
            Destroy(child.gameObject);
        }

        var history = CompanyManager.Instance.GetCompanyData().History;
        if (history == null || history.Count == 0) return;

        var recentTransactions = history
            .Skip(Math.Max(0, history.Count - maxScrollItems))
            .Reverse()
            .ToList();

        foreach (var tx in recentTransactions)
        {
            var card = Instantiate(transactionPrefab, transactionContentContainer);
            card.transform.localScale = Vector3.one;
            card.Setup(tx);
        }
    }

    private void RefreshChart()
    {
        if (financeChart == null) return;

        // Rename the chart header title
        var title = financeChart.EnsureChartComponent<Title>();
        title.show = true;
        title.text = "Transactions";

        var history = CompanyManager.Instance.GetCompanyData().History;
        if (history == null) return;

        string selectedFilter = _dropdownOptions[categoryDropdown.value];
        List<Transaction> filteredHistory = new List<Transaction>();

        if (selectedFilter == "All")
        {
            filteredHistory = history;
        }
        else
        {
            if (Enum.TryParse(selectedFilter, out TransactionCategory parsedCategory))
            {
                filteredHistory = history.Where(t => t.Category == parsedCategory).ToList();
            }
        }

        var chartData = filteredHistory
            .Skip(Math.Max(0, filteredHistory.Count - maxChartItems))
            .ToList();

        financeChart.ClearData();
        
        if (financeChart.series.Count == 0)
        {
            financeChart.AddSerie<Line>("Amount");
        }

        financeChart.series[0].serieName = selectedFilter == "All" ? "Net Cashflow" : $"{selectedFilter} Cashflow";

        foreach (var tx in chartData)
        {
            financeChart.AddXAxisData($"Day {tx.Timestamp}");
            financeChart.AddData(0, tx.Amount);
        }
    }
}