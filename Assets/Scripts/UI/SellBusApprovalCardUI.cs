using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SellBusApprovalCardUI : MonoBehaviour
{
    public TextMeshProUGUI summaryText;
    
    [Header("Toggle Array (Max 5)")]
    public Toggle[] busToggles;
    public TextMeshProUGUI[] busToggleTexts;

    [Header("Controls")]
    public Button approveButton;
    public Button rejectButton;

    private GameRequest _request;

    public void Setup(GameRequest request)
    {
        _request = request;
        
        summaryText.text = $"{request.Requester} requests permission to sell {request.TargetAmount} buses.";

        // Split the comma-separated payload into individual bus IDs
        string[] requestedBuses = request.Payload.Split(',');

        // Enable only the required number of toggles
        for (int i = 0; i < busToggles.Length; i++)
        {
            if (i < requestedBuses.Length && !string.IsNullOrEmpty(requestedBuses[i]))
            {
                busToggles[i].gameObject.SetActive(true);
                busToggles[i].isOn = false; // Default to checked
                busToggleTexts[i].text = requestedBuses[i].Trim();
            }
            else
            {
                // Hide unused toggles!
                busToggles[i].gameObject.SetActive(false);
            }
        }

        approveButton.onClick.RemoveAllListeners();
        approveButton.onClick.AddListener(OnApprove);

        rejectButton.onClick.RemoveAllListeners();
        rejectButton.onClick.AddListener(() => RequestManager.Instance.RejectRequest(_request.RequestID, "Sale Denied"));
    }

    private void OnApprove()
    {
        List<string> approvedBusIDs = new List<string>();

        // Check which toggles the manager left checked
        for (int i = 0; i < busToggles.Length; i++)
        {
            if (busToggles[i].gameObject.activeSelf && busToggles[i].isOn)
            {
                approvedBusIDs.Add(busToggleTexts[i].text);
            }
        }

        if (approvedBusIDs.Count == 0)
        {
            RequestManager.Instance.RejectRequest(_request.RequestID, "0 Buses Approved for Sale");
        }
        else
        {
            string modifiedPayload = string.Join(",", approvedBusIDs);
            RequestManager.Instance.ApproveForwardRequest(_request.RequestID, approvedBusIDs.Count, modifiedPayload);
        }
    }
}