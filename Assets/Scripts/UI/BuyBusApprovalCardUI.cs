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

        // Force slider to start at 1, max is the requested amount
        approveSlider.minValue = 1;
        approveSlider.maxValue = Mathf.Max(1, request.TargetAmount); 
        approveSlider.value = 1; 

        approveSlider.onValueChanged.RemoveAllListeners();
        approveSlider.onValueChanged.AddListener(val => sliderValueText.text = val.ToString("0"));
        sliderValueText.text = approveSlider.value.ToString("0");

        approveButton.onClick.RemoveAllListeners();
        approveButton.onClick.AddListener(OnApprove);

        rejectButton.onClick.RemoveAllListeners();
        rejectButton.onClick.AddListener(OnReject);
    }

    private void OnApprove()
    {
        int approvedAmount = Mathf.RoundToInt(approveSlider.value);
        RequestManager.Instance.ApproveForwardRequest(_request.RequestID, approvedAmount);
    }

    private void OnReject()
    {
        RequestManager.Instance.RejectRequest(_request.RequestID, "Purchase Denied by Finance");
    }
}