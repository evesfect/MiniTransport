using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LoanApprovalCardUI : MonoBehaviour
{
    public TextMeshProUGUI summaryText;
    public Button approveButton;
    public Button rejectButton;

    private GameRequest _request;

    public void Setup(GameRequest request)
    {
        _request = request;

        // The payload string contains: "rate,weeks,totalPayback,weeklyPayment"
        string[] data = request.Payload.Split(',');
        if (data.Length >= 4)
        {
            summaryText.text = $"Finance Manager requests a {request.TargetAmount}$ loan.\n" +
                               $"Interest: {data[0]}% | Duration: {data[1]} weeks\n" +
                               $"Total Payback: {data[2]}$\n" +
                               $"Weekly Payment: {data[3]}$";
        }
        else
        {
            // Fallback just in case of a network hiccup
            summaryText.text = $"Finance Manager requests a {request.TargetAmount}$ loan.";
        }

        approveButton.onClick.RemoveAllListeners();
        approveButton.onClick.AddListener(OnApprove);

        rejectButton.onClick.RemoveAllListeners();
        rejectButton.onClick.AddListener(OnReject);
    }

    private void OnApprove()
    {
        // Approve the exact requested principal amount
        RequestManager.Instance.ApproveForwardRequest(_request.RequestID, _request.TargetAmount);
    }

    private void OnReject()
    {
        RequestManager.Instance.RejectRequest(_request.RequestID, "Loan Denied by General Manager");
    }
}