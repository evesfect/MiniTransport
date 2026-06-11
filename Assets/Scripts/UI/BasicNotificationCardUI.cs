using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
            case RequestType.HireMechanic: return $"Hire {r.TargetAmount} Mechanics (Min Skill: {r.Payload})";
            case RequestType.TrainMechanic: return $"Train {r.TargetAmount} Mechanics";
            case RequestType.BuyParts: return $"Purchase {r.TargetAmount}x {r.Payload}";
            case RequestType.BuyBus: return $"Purchase {r.TargetAmount} New Buses";
            case RequestType.SellBus: return $"Sell {r.TargetAmount} Buses ({r.Payload})";
            default: return "Unknown Request";
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