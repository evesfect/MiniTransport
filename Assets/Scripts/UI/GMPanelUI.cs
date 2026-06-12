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

    [Header("Fare Controls")]
    [SerializeField] private TMP_InputField ticketPriceInput;
    [SerializeField] private TMP_InputField transferDiscountInput;
    [SerializeField] private Button setPriceButton;
    [SerializeField] private Button setDiscountButton;

    [Header("Formatting")]
    [SerializeField] private string moneyPrefix = "$";
    [Tooltip("{0} = formatted balance (with money prefix).")]
    [SerializeField] private string balanceFormat = "Balance: {0}";
    [Tooltip("{0} = satisfaction percentage.")]
    [SerializeField] private string satisfactionFormat = "Customer Satisfaction: {0}%";
    [Tooltip("{0} = demand-met percentage.")]
    [SerializeField] private string demandFormat = "Percentage of demand met: {0}%";
    [Tooltip("If true, the transfer-discount text box uses 0-100 (a percentage). If false, 0-1.")]
    [SerializeField] private bool discountAsPercent = true;

    // Balance reaches a non-host client only if this client registers interest in CompanyStats.
    private bool _companyInterestRegistered;
    // NaN forces the next sync to push the value into the text box.
    private float _lastShownPrice = float.NaN;
    private float _lastShownDiscount = float.NaN;

    private void OnEnable()
    {
        if (setPriceButton != null) setPriceButton.onClick.AddListener(ApplyTicketPrice);
        if (setDiscountButton != null) setDiscountButton.onClick.AddListener(ApplyTransferDiscount);

        // Force the input fields to repopulate from the current networked values.
        _lastShownPrice = float.NaN;
        _lastShownDiscount = float.NaN;
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
            if (balanceText != null)
            {
                var data = company.GetCompanyData();
                if (data != null)
                    balanceText.text = string.Format(balanceFormat, moneyPrefix + ReportFormat.Money(data.CurrentBalance));
            }

            if (satisfactionText != null)
                satisfactionText.text = string.Format(satisfactionFormat, Mathf.RoundToInt(company.Satisfaction));

            SyncInputFields(company);
        }

        if (demandMetText != null && KPIManager.Instance != null)
            demandMetText.text = string.Format(demandFormat, Mathf.RoundToInt(KPIManager.Instance.DemandMetPercent));
    }

    // Reflect the confirmed networked values into the text boxes, but never clobber the GM while typing.
    private void SyncInputFields(CompanyManager company)
    {
        if (ticketPriceInput != null && !ticketPriceInput.isFocused)
        {
            float price = company.TicketPrice;
            if (!Mathf.Approximately(price, _lastShownPrice))
            {
                ticketPriceInput.text = price.ToString("0.##");
                _lastShownPrice = price;
            }
        }

        if (transferDiscountInput != null && !transferDiscountInput.isFocused)
        {
            float discount = company.TransferDiscount;
            if (!Mathf.Approximately(discount, _lastShownDiscount))
            {
                float shown = discountAsPercent ? discount * 100f : discount;
                transferDiscountInput.text = shown.ToString("0.##");
                _lastShownDiscount = discount;
            }
        }
    }

    private void ApplyTicketPrice()
    {
        if (CompanyManager.Instance == null || ticketPriceInput == null) return;
        if (float.TryParse(ticketPriceInput.text, out float price))
        {
            CompanyManager.Instance.RequestSetTicketPriceRpc(Mathf.Max(0f, price));
            _lastShownPrice = float.NaN; // refresh from the confirmed value once it syncs back
        }
    }

    private void ApplyTransferDiscount()
    {
        if (CompanyManager.Instance == null || transferDiscountInput == null) return;
        if (float.TryParse(transferDiscountInput.text, out float value))
        {
            float discount = discountAsPercent ? value / 100f : value;
            CompanyManager.Instance.RequestSetTransferDiscountRpc(Mathf.Clamp01(discount));
            _lastShownDiscount = float.NaN;
        }
    }

    // --- CompanyStats subscription (for the balance on clients) ---

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
