using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FinancePartsRequestUI : MonoBehaviour
{
    [Header("Controls")]
    public TMP_Dropdown partDropdown;
    public Slider quantitySlider;
    public TextMeshProUGUI quantityText;
    public TextMeshProUGUI requestOverviewText;
    public Button sendRequestButton;

    private List<string> _availablePartNames = new List<string>();

    private void OnEnable()
    {
        // Add listeners
        quantitySlider.onValueChanged.AddListener(OnQuantityChanged);
        partDropdown.onValueChanged.AddListener(OnPartChanged);
        sendRequestButton.onClick.AddListener(SendPartsRequest);

        SetupDropdown();
        UpdatePartsUI();
    }

    private void OnDisable()
    {
        // Remove listeners
        quantitySlider.onValueChanged.RemoveListener(OnQuantityChanged);
        partDropdown.onValueChanged.RemoveListener(OnPartChanged);
        sendRequestButton.onClick.RemoveListener(SendPartsRequest);
    }

    private void SetupDropdown()
    {
        if (VendorManager.Instance == null) return;

        partDropdown.ClearOptions();
        _availablePartNames.Clear();

        // Extract available part names from VendorManager
        // Flatten the dictionary values into a single list of strings
        foreach (var partsArray in VendorManager.CategoryParts.Values)
        {
            _availablePartNames.AddRange(partsArray);
        }

        // Remove duplicates if any (e.g., Dashboards might be engine and chassis parts in some contexts)
        List<string> uniqueParts = _availablePartNames.Distinct().ToList();
        
        partDropdown.AddOptions(uniqueParts);
    }

    private void OnQuantityChanged(float value)
    {
        UpdatePartsUI();
    }

    private void OnPartChanged(int value)
    {
        UpdatePartsUI();
    }

    private void UpdatePartsUI()
    {
        // Quantity slider (1-50)
        int partQuantity = Mathf.RoundToInt(quantitySlider.value);
        quantityText.text = $"Quantity: {partQuantity.ToString()}";

        // Dropdown part
        string selectedPart = partDropdown.options[partDropdown.value].text;
        
        // Set summary
        requestOverviewText.text = $"Summary:\nRequesting to buy {partQuantity}x {selectedPart} from Vendors.";
    }

    private void SendPartsRequest()
    {
        int quantity = Mathf.RoundToInt(quantitySlider.value);
        string selectedPart = partDropdown.options[partDropdown.value].text;

        // Payload is the part name (ItemID)
        RequestManager.Instance.CreateRequest(RequestType.BuyParts, PlayerRole.FinanceManager, quantity, selectedPart);
        
        // Optional clear
    }
}