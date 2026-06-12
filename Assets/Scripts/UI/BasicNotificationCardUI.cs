using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Collections.Generic;


public class BasicNotificationCardUI : MonoBehaviour
{
    public TextMeshProUGUI summaryText;
    public TextMeshProUGUI trackingText;
    
    public Button rejectButton;
    public Button markReadButton;

    private GameRequest _request;

    public void Setup(GameRequest request, PlayerRole myRole)
    {
        _request = request;

        // 1. Setup Summary Text
        summaryText.text = $"{request.Requester} Request:\n{GetSummary(request)}";

        // 2. Setup Tracking Text
        trackingText.text = GetTrackingStatus(request);

        // 3. Button Visibility Logic
        rejectButton.gameObject.SetActive(false);
        markReadButton.gameObject.SetActive(false);

        if (myRole == request.Requester)
        {
            // Requester View
            markReadButton.gameObject.SetActive(true);
            
            // Only allow marking as read if it's completely finished
            markReadButton.interactable = (request.State == RequestState.Completed || request.State == RequestState.Rejected);
        }
        else if (myRole == request.CurrentTarget)
        {
            // Responder View (HR/Finance for basic requests)
            if (request.State == RequestState.Active)
            {
                rejectButton.gameObject.SetActive(true);
            }
        }

        rejectButton.onClick.RemoveAllListeners();
        rejectButton.onClick.AddListener(() => RequestManager.Instance.RejectRequest(_request.RequestID, "Rejected by Manager."));

        markReadButton.onClick.RemoveAllListeners();
        markReadButton.onClick.AddListener(() => RequestManager.Instance.MarkAsRead(_request.RequestID));
    }

    private string GetSummary(GameRequest r)
    {
        switch (r.Type)
        {
            case RequestType.HireMechanic: 
                return $"Hire {r.TargetAmount} Mechanics (Min Skill: {r.Payload})";
                
            case RequestType.TrainMechanic:
                string employeeDisplay = "Unknown";
                
                // Decode the IDs from the payload back into numeric names
                if (!string.IsNullOrEmpty(r.Payload) && EmployeeManager.Instance != null)
                {
                    string[] ids = r.Payload.Split(',');
                    List<string> mechanicNumbers = new List<string>();
                    
                    foreach (string id in ids)
                    {
                        var emp = EmployeeManager.Instance.allEmployees.FirstOrDefault(e => e.EmployeeID == id.Trim());
                        if (emp != null)
                        {
                            // Extract only the numbers
                            string numberOnly = new string(emp.FullName.Where(char.IsDigit).ToArray());
                            mechanicNumbers.Add(string.IsNullOrEmpty(numberOnly) ? emp.FullName : numberOnly);
                        }
                    }
                    
                    if (mechanicNumbers.Count > 0)
                    {
                        employeeDisplay = string.Join(", ", mechanicNumbers);
                    }
                }
                return $"Train {r.TargetAmount} Mechanics\nMechanic(s): {employeeDisplay}";
                
            case RequestType.BuyParts: 
                return $"Purchase {r.TargetAmount}x {r.Payload}";
                
            case RequestType.BuyBus: 
                return $"Purchase {r.TargetAmount} New Buses";
                
            case RequestType.SellBus: 
                return $"Sell {r.TargetAmount} Buses ({r.Payload})";
                
            default: 
                return "Unknown Request";
        }
    }

    private string GetTrackingStatus(GameRequest r)
    {
        if (r.State == RequestState.Rejected)
            return $"<color=red>Progress: {r.CurrentAmount}/{r.TargetAmount} (Rejected)</color>";
        
        if (r.State == RequestState.Completed)
            return $"<color=green>Progress: {r.CurrentAmount}/{r.TargetAmount} (Completed)</color>";

        if (r.State == RequestState.AwaitingGeneralManager)
            return $"<color=yellow>Awaiting GM Approval ({r.TargetAmount} approved by Finance)</color>";

        return $"Progress: {r.CurrentAmount}/{r.TargetAmount} (Awaiting {r.CurrentTarget})";
    }
}