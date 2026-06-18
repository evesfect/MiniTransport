using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Canvas/uGUI controller for the General Manager panel.
///
/// Shows live, networked company stats (balance, customer satisfaction, percentage of demand met)
/// and lets the GM set the ticket price and the transfer discount. Every value is networked, so the
/// panel works on the host and on non-host clients alike.
///
/// Stats are polled in Update with null-checks (the data sources are NetworkBehaviours that spawn
/// after this component). Wire the serialized fields in the Inspector.
/// </summary>

public class GMPanelUI : MonoBehaviour
{
    [Header("Stat Labels (read-only display)")]
    [SerializeField] private TMP_Text balanceText;
    [SerializeField] private TMP_Text satisfactionText;
    [SerializeField] private TMP_Text demandMetText;
    
    [Header("Fare Labels (read-only display)")]
    [SerializeField] private TMP_Text currentTicketPriceText; // [NEW] Link this to "CurrentPrice" Text
    [SerializeField] private TMP_Text currentDiscountText;    // [NEW] Link this to "Transit" Text

    [Header("Fare Controls")]
    [SerializeField] private TMP_InputField ticketPriceInput;
    [SerializeField] private TMP_InputField transferDiscountInput;
    [SerializeField] private Button setPriceButton;
    [SerializeField] private Button setDiscountButton;

    [Header("Formatting")]
    [SerializeField] private string moneyPrefix = "$";
    [SerializeField] private string balanceFormat = "Current Balance: {0}";
    [SerializeField] private string satisfactionFormat = "Customer Satisfaction (out of 100): {0}";
    [SerializeField] private string demandFormat = "Percentage of demand met: {0}%";
    [SerializeField] private string ticketPriceFormat = "Current Ticket Price: {0}$"; // [NEW]
    [SerializeField] private string discountFormat = "Transit discount rate: {0}%";   // [NEW]
    [SerializeField] private bool discountAsPercent = true;

    private bool _companyInterestRegistered;

    private void OnEnable()
    {
        if (setPriceButton != null) setPriceButton.onClick.AddListener(ApplyTicketPrice);
        if (setDiscountButton != null) setDiscountButton.onClick.AddListener(ApplyTransferDiscount);
    }

    private void OnDisable()
    {
        if (setPriceButton != null) setPriceButton.onClick.RemoveListener(ApplyTicketPrice);
        if (setDiscountButton != null) setDiscountButton.onClick.RemoveListener(ApplyTransferDiscount);
        ReleaseCompanyInterest();
    }

    private void Update()
    {
        EnsureCompanyInterest();

        var company = CompanyManager.Instance;
        if (company != null)
        {
            // 1. General Stats
            if (balanceText != null)
            {
                var data = company.GetCompanyData();
                if (data != null)
                    balanceText.text = string.Format(balanceFormat, moneyPrefix + ReportFormat.Money(data.CurrentBalance));
            }

            if (satisfactionText != null)
                satisfactionText.text = string.Format(satisfactionFormat, Mathf.RoundToInt(company.Satisfaction));

            // 2. [NEW] Live Fare Updates
            if (currentTicketPriceText != null)
            {
                currentTicketPriceText.text = string.Format(ticketPriceFormat, company.TicketPrice.ToString("0.##"));
            }

            if (currentDiscountText != null)
            {
                float shownDiscount = discountAsPercent ? company.TransferDiscount * 100f : company.TransferDiscount;
                currentDiscountText.text = string.Format(discountFormat, shownDiscount.ToString("0.##"));
            }
        }

        if (demandMetText != null && KPIManager.Instance != null)
            demandMetText.text = string.Format(demandFormat, Mathf.RoundToInt(KPIManager.Instance.DemandMetPercent));
    }

    private void ApplyTicketPrice()
    {
        if (CompanyManager.Instance == null || ticketPriceInput == null) return;
        if (float.TryParse(ticketPriceInput.text, out float price))
        {
            CompanyManager.Instance.RequestSetTicketPriceRpc(Mathf.Max(0f, price));
            ticketPriceInput.text = ""; // Clear the input field for visual feedback
        }
    }

    private void ApplyTransferDiscount()
    {
        if (CompanyManager.Instance == null || transferDiscountInput == null) return;
        if (float.TryParse(transferDiscountInput.text, out float value))
        {
            float discount = discountAsPercent ? value / 100f : value;
            CompanyManager.Instance.RequestSetTransferDiscountRpc(Mathf.Clamp01(discount));
            transferDiscountInput.text = ""; // Clear the input field for visual feedback
        }
    }

    // --- CompanyStats subscription ---
    private void EnsureCompanyInterest()
    {
        if (_companyInterestRegistered || LocalDataBroker.Instance == null) return;
        LocalDataBroker.Instance.RegisterInterest(SyncDataType.CompanyStats);
        _companyInterestRegistered = true;
    }

    private void ReleaseCompanyInterest()
    {
        if (!_companyInterestRegistered || LocalDataBroker.Instance == null) return;
        LocalDataBroker.Instance.UnregisterInterest(SyncDataType.CompanyStats);
        _companyInterestRegistered = false;
    }
}