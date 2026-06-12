using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
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

    [Header("Refresh Triggers")]
    [Tooltip("Drag your Reload button here.")]
    public Button reloadButton; 
    [Tooltip("Drag the Animated Toggle Button that opens this panel here.")]
    public AnimatedToggleButton panelToggleButton; // [NEW]

    private List<string> _dropdownOptions = new List<string>();

    // Tracks whether this client is enrolled for ongoing ledger syncs from the server. Without this,
    // a remote client only ever receives the one-time initial ledger snapshot (sent on connect) and
    // its table goes stale the moment new transactions happen server-side. The host reads the live
    // ledger directly and doesn't need (or send) a subscription.
    private bool _ledgerSubscribed = false;

    private void OnEnable()
    {
        SetupDropdown();
        EnsureLedgerSubscription();

        if (CompanyManager.Instance != null)
        {
            Debug.Log($"[FinanceDashboardUI] OnEnable subscribing to CompanyManager events. Current ledger items: {CompanyManager.Instance.GetCompanyData().History.Count}");
            CompanyManager.Instance.OnLedgerUpdated -= HandleCompanyDataUpdated;
            CompanyManager.Instance.OnLedgerUpdated += HandleCompanyDataUpdated;

            CompanyManager.Instance.OnBalanceChanged -= HandleCompanyDataUpdated;
            CompanyManager.Instance.OnBalanceChanged += HandleCompanyDataUpdated;
        }
        else
        {
            Debug.LogError("[FinanceDashboardUI] OnEnable: CompanyManager.Instance is NULL! Cannot subscribe to events.");
        }

        // Hook up the manual reload button
        if (reloadButton != null)
        {
            reloadButton.onClick.RemoveAllListeners();
            reloadButton.onClick.AddListener(ForceRefresh);
        }

        // [NEW] Hook up the panel toggle button to listen for the "Open" state
        if (panelToggleButton != null)
        {
            panelToggleButton.onValueChanged.RemoveListener(OnPanelToggled);
            panelToggleButton.onValueChanged.AddListener(OnPanelToggled);
        }

        // Initial draw just in case the panel is forced open on start
        if (CompanyManager.Instance != null)
        {
            ForceRefresh(); 
        }
    }

    private void OnDisable()
    {
        if (reloadButton != null)
        {
            reloadButton.onClick.RemoveListener(ForceRefresh);
        }

        if (panelToggleButton != null)
        {
            panelToggleButton.onValueChanged.RemoveListener(OnPanelToggled);
        }

        if (CompanyManager.Instance != null)
        {
            CompanyManager.Instance.OnLedgerUpdated -= HandleCompanyDataUpdated;
            CompanyManager.Instance.OnBalanceChanged -= HandleCompanyDataUpdated;
        }

        ReleaseLedgerSubscription();
    }

    // [NEW] Listens to the toggle button and refreshes only when opening
    private void OnPanelToggled(bool isOpen)
    {
        if (isOpen && CompanyManager.Instance != null)
        {
            // The panel object can be enabled (and OnEnable run) before the client finishes
            // connecting, in which case the initial subscribe attempt is skipped. Retry on open.
            EnsureLedgerSubscription();
            ForceRefresh();
        }
    }

    // Enrolls a remote client for ongoing CompanyLedger syncs. Subscribing also makes the server
    // immediately push the current ledger (NetworkSyncBroker.SubscribeRpc delivers current state),
    // so the table fills in right away. No-op on the host/server, which holds the live ledger.
    private void EnsureLedgerSubscription()
    {
        if (_ledgerSubscribed) return;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer || NetworkSyncBroker.Instance == null) return;

        NetworkSyncBroker.Instance.SubscribeRpc(SyncDataType.CompanyLedger);
        _ledgerSubscribed = true;
    }

    private void ReleaseLedgerSubscription()
    {
        if (!_ledgerSubscribed) return;
        _ledgerSubscribed = false;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer || NetworkSyncBroker.Instance == null) return;

        NetworkSyncBroker.Instance.UnsubscribeRpc(SyncDataType.CompanyLedger);
    }

    private void HandleCompanyDataUpdated()
    {
        Debug.Log($"[FinanceDashboardUI] HandleCompanyDataUpdated called. isActiveAndEnabled={isActiveAndEnabled}");
        if (isActiveAndEnabled)
        {
            Debug.Log($"[FinanceDashboardUI] Calling ForceRefresh(). Ledger items: {CompanyManager.Instance.GetCompanyData().History.Count}");
            ForceRefresh();
        }
    }

    private void HandleCompanyDataUpdated(float _)
    {
        HandleCompanyDataUpdated();
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

    public void ForceRefresh()
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