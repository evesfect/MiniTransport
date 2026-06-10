using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TransportRequestUI : MonoBehaviour
{
    [Header("Controls")]
    public Slider quantitySlider; // Slider allowed between 1 and 5
    public TextMeshProUGUI quantityText;
    public Button sendRequestButton;

    private void OnEnable()
    {
        // Add listeners
        quantitySlider.onValueChanged.AddListener(UpdateBuyBusUI);
        sendRequestButton.onClick.AddListener(SendBuyBusRequest);
        
        UpdateBuyBusUI(quantitySlider.value); 
    }

    private void OnDisable()
    {
        // Remove listeners
        quantitySlider.onValueChanged.RemoveListener(UpdateBuyBusUI);
        sendRequestButton.onClick.RemoveListener(SendBuyBusRequest);
    }

    private void UpdateBuyBusUI(float value)
    {
        // Quantity slider (1-5)
        int buyQuantity = Mathf.RoundToInt(quantitySlider.value);
        quantityText.text = $"Quantity: {buyQuantity.ToString()}";
    }

    private void SendBuyBusRequest()
    {
        int quantity = Mathf.RoundToInt(quantitySlider.value);

        // Targeted to FinanceManager. Payload description is handled in request summary.
        RequestManager.Instance.CreateRequest(RequestType.BuyBus, PlayerRole.FinanceManager, quantity, $"Requested buy for {quantity} buses.");
        
        // Optional clear selection
    }
}