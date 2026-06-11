using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BuyBusApprovalCardUI : MonoBehaviour
{
    public TextMeshProUGUI summaryText;
    public Slider approveSlider;
    public TextMeshProUGUI sliderValueText;
    
    public Button approveButton;
    public Button rejectButton;

    private GameRequest _request;

    public void Setup(GameRequest request)
    {
        _request = request;
        
        summaryText.text = $"{request.Requester} requests the purchase of {request.TargetAmount} new buses.";

        // Setup dynamic slider constraints
        approveSlider.minValue = 0;
        approveSlider.maxValue = request.TargetAmount;
        approveSlider.value = request.TargetAmount; // Default to full approval

        approveSlider.onValueChanged.RemoveAllListeners();
        approveSlider.onValueChanged.AddListener(val => sliderValueText.text = val.ToString("0"));
        sliderValueText.text = approveSlider.value.ToString("0");

        approveButton.onClick.RemoveAllListeners();
        approveButton.onClick.AddListener(OnApprove);

        rejectButton.onClick.RemoveAllListeners();
        rejectButton.onClick.AddListener(() => RequestManager.Instance.RejectRequest(_request.RequestID, "Purchase Denied"));
    }

    private void OnApprove()
    {
        int approvedAmount = Mathf.RoundToInt(approveSlider.value);
        if (approvedAmount == 0)
        {
            RequestManager.Instance.RejectRequest(_request.RequestID, "0 Buses Approved");
        }
        else
        {
            RequestManager.Instance.ApproveForwardRequest(_request.RequestID, approvedAmount);
        }
    }
}