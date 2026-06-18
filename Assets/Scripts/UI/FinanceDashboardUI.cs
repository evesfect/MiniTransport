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
    public Button reloadButton; 
    public AnimatedToggleButton panelToggleButton;

    [Header("Loan Controls")] // [NEW]
    public Slider loanSlider;
    public TextMeshProUGUI loanAmountText;
    public TextMeshProUGUI loanDetailsText;
    public Button sendLoanButton;

    private List<string> _dropdownOptions = new List<string>();
    private bool _ledgerSubscribed = false;

    private void OnEnable()
    {
        SetupDropdown();
        EnsureLedgerSubscription();

        if (CompanyManager.Instance != null)
        {
            CompanyManager.Instance.OnLedgerUpdated -= HandleCompanyDataUpdated;
            CompanyManager.Instance.OnLedgerUpdated += HandleCompanyDataUpdated;

            CompanyManager.Instance.OnBalanceChanged -= HandleCompanyDataUpdated;
            CompanyManager.Instance.OnBalanceChanged += HandleCompanyDataUpdated;
        }

        if (reloadButton != null)
        {
            reloadButton.onClick.RemoveAllListeners();
            reloadButton.onClick.AddListener(ForceRefresh);
        }

        if (panelToggleButton != null)
        {
            panelToggleButton.onValueChanged.RemoveListener(OnPanelToggled);
            panelToggleButton.onValueChanged.AddListener(OnPanelToggled);
        }

        // [NEW] Setup Loan Slider
        if (loanSlider != null)
        {
            loanSlider.minValue = 1000;
            loanSlider.maxValue = 50000;
            loanSlider.onValueChanged.RemoveAllListeners();
            loanSlider.onValueChanged.AddListener(UpdateLoanUI);
            UpdateLoanUI(loanSlider.value); // Force initial draw
        }

        if (sendLoanButton != null)
        {
            sendLoanButton.onClick.RemoveAllListeners();
            sendLoanButton.onClick.AddListener(SendLoanRequest);
        }

        if (CompanyManager.Instance != null) ForceRefresh(); 
    }

    private void OnDisable()
    {
        if (reloadButton != null) reloadButton.onClick.RemoveListener(ForceRefresh);
        if (panelToggleButton != null) panelToggleButton.onValueChanged.RemoveListener(OnPanelToggled);
        
        if (CompanyManager.Instance != null)
        {
            CompanyManager.Instance.OnLedgerUpdated -= HandleCompanyDataUpdated;
            CompanyManager.Instance.OnBalanceChanged -= HandleCompanyDataUpdated;
        }

        ReleaseLedgerSubscription();
    }

    // [NEW] Calculates real-time interest and duration
    private void UpdateLoanUI(float val)
    {
        // 1. Check if they already have a loan and lock the UI if they do
        if (CompanyManager.Instance != null && CompanyManager.Instance.HasActiveLoan)
        {
            loanSlider.interactable = false;
            sendLoanButton.interactable = false;
            
            if (loanAmountText != null) loanAmountText.text = "Loan Unavailable";
            if (loanDetailsText != null) loanDetailsText.text = "You already have an active loan. You must pay it off completely before requesting another.";
            return;
        }

        // 2. Unlock the UI if they are debt-free
        loanSlider.interactable = true;
        sendLoanButton.interactable = true;

        int amount = Mathf.RoundToInt(val);
        if (loanAmountText != null) loanAmountText.text = $"Amount: {amount} $";

        // Map amount from 1000-50000 to interest rate 1.5 - 0.5
        float rate = 1.5f - ((amount - 1000f) / 49000f) * 1.0f;
        
        int currentDay = SimulationTimeManager.Instance != null ? SimulationTimeManager.Instance.CurrentDay : 0;
        int totalDays = GameEndManager.Instance != null ? GameEndManager.Instance.gameLengthDays : 30;
        
        int daysLeft = Mathf.Max(0, totalDays - currentDay);
        int weeksLeft = Mathf.Max(1, daysLeft / 7); // Minimum 1 week

        // 3. Cap the maximum loan duration at 10 weeks
        weeksLeft = Mathf.Min(weeksLeft, 10);

        float totalInterest = amount * (rate / 100f) * weeksLeft;
        float totalPayback = amount + totalInterest;
        float weeklyPayment = totalPayback / weeksLeft;

        if (loanDetailsText != null)
        {
            loanDetailsText.text = $"Weekly interest rate: {rate:F1}%\nTotal pay-back in {weeksLeft} weeks: {totalPayback:F0} $\nWeekly payment: {weeklyPayment:F0} $";
        }
    }

    // [NEW] Dispatches the request to the GM
    private void SendLoanRequest()
    {
        // Double safety check
        if (CompanyManager.Instance.HasActiveLoan) return;

        int amount = Mathf.RoundToInt(loanSlider.value);
        float rate = 1.5f - ((amount - 1000f) / 49000f) * 1.0f;
        
        int currentDay = SimulationTimeManager.Instance != null ? SimulationTimeManager.Instance.CurrentDay : 0;
        int totalDays = GameEndManager.Instance != null ? GameEndManager.Instance.gameLengthDays : 30;
        int weeksLeft = Mathf.Max(1, (totalDays - currentDay) / 7);

        weeksLeft = Mathf.Min(weeksLeft, 10); // Apply the 10-week cap to the data payload too

        float totalInterest = amount * (rate / 100f) * weeksLeft;
        float totalPayback = amount + totalInterest;
        float weeklyPayment = totalPayback / weeksLeft;

        // Bundle up the math for the GM card and the backend processor
        string payload = $"{rate:F1},{weeksLeft},{totalPayback:F0},{weeklyPayment:F0}";

        // Directly target the GM (One-tier approval)
        RequestManager.Instance.CreateRequest(RequestType.TakeLoan, PlayerRole.GeneralManager, amount, payload);
    }

    private void OnPanelToggled(bool isOpen)
    {
        if (isOpen && CompanyManager.Instance != null)
        {
            EnsureLedgerSubscription();
            ForceRefresh();
        }
    }

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
        if (isActiveAndEnabled) ForceRefresh();
    }

    private void HandleCompanyDataUpdated(float _) => HandleCompanyDataUpdated();

    private void SetupDropdown()
    {
        categoryDropdown.ClearOptions();
        _dropdownOptions.Clear();
        _dropdownOptions.Add("All");
        foreach (string cat in Enum.GetNames(typeof(TransactionCategory))) _dropdownOptions.Add(cat);
        categoryDropdown.AddOptions(_dropdownOptions);
        categoryDropdown.onValueChanged.RemoveAllListeners();
        categoryDropdown.onValueChanged.AddListener(delegate { ForceRefresh(); }); 
    }

    public void ForceRefresh()
    {
        RefreshScrollView();
        RefreshChart();
        // Force the loan UI to redraw so it instantly unlocks if a debt was just paid off!
        if (loanSlider != null) UpdateLoanUI(loanSlider.value);
    }

    private void RefreshScrollView()
    {
        foreach (Transform child in transactionContentContainer) Destroy(child.gameObject);

        var history = CompanyManager.Instance.GetCompanyData().History;
        if (history == null || history.Count == 0) return;

        var recentTransactions = history.Skip(Math.Max(0, history.Count - maxScrollItems)).Reverse().ToList();
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

        if (selectedFilter == "All") filteredHistory = history;
        else if (Enum.TryParse(selectedFilter, out TransactionCategory parsedCategory))
            filteredHistory = history.Where(t => t.Category == parsedCategory).ToList();

        var chartData = filteredHistory.Skip(Math.Max(0, filteredHistory.Count - maxChartItems)).ToList();
        financeChart.ClearData();
        if (financeChart.series.Count == 0) financeChart.AddSerie<Line>("Amount");
        financeChart.series[0].serieName = selectedFilter == "All" ? "Net Cashflow" : $"{selectedFilter} Cashflow";

        foreach (var tx in chartData)
        {
            financeChart.AddXAxisData($"Day {tx.Timestamp}");
            financeChart.AddData(0, tx.Amount);
        }
    }
}