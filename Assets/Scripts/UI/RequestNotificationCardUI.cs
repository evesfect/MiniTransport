using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RequestNotificationCardUI : MonoBehaviour
{
    public TextMeshProUGUI logMessageText;
    
    [Header("Action Buttons")]
    public GameObject responderControlsGroup; // Container holding Approve & Reject
    public Button approveButton;
    public Button rejectButton;
    public Button markReadButton; // Container holding Requester archived notice

    private GameRequest _associatedRequest;

    public void Setup(GameRequest request)
    {
        _associatedRequest = request;
        logMessageText.text = request.GetNotificationText();
        
        PlayerRole myRole = RoleManager.Instance.GetMyRole();

        // 1. Clear button contexts
        responderControlsGroup.SetActive(false);
        markReadButton.gameObject.SetActive(false);

        // 2. Determine visibility rules
        if (request.State == RequestState.Active || request.State == RequestState.AwaitingGeneralManager)
        {
            if (request.CurrentTarget == myRole)
            {
                responderControlsGroup.SetActive(true);
                
                // Hide Approve for HR/Finance Part buying since they work off direct action completions
                bool usesDirectAction = (request.Type == RequestType.HireMechanic || request.Type == RequestType.TrainMechanic || request.Type == RequestType.BuyParts);
                approveButton.gameObject.SetActive(!usesDirectAction);
            }
        }
        else if (request.State == RequestState.Completed || request.State == RequestState.Rejected)
        {
            if (request.Requester == myRole)
            {
                markReadButton.gameObject.SetActive(true);
                markReadButton.interactable = true;
            }
        }

        // Bind Actions
        approveButton.onClick.RemoveAllListeners();
        approveButton.onClick.AddListener(OnApproveClicked);

        rejectButton.onClick.RemoveAllListeners();
        rejectButton.onClick.AddListener(OnRejectClicked);

        markReadButton.onClick.RemoveAllListeners();
        markReadButton.onClick.AddListener(OnMarkReadClicked);
    }

    private void OnApproveClicked()
    {
        // Defaulting to max target payload for complete UI workflow.
        // For partials, extract selections dynamically from active panel states.
        RequestManager.Instance.ApproveForwardRequest(_associatedRequest.RequestID, _associatedRequest.TargetAmount, _associatedRequest.Payload);
    }

    private void OnRejectClicked()
    {
        RequestManager.Instance.RejectRequest(_associatedRequest.RequestID, "Budget constraints or logistical scheduling conflict.");
    }

    private void OnMarkReadClicked()
    {
        RequestManager.Instance.MarkAsRead(_associatedRequest.RequestID);
    }
}