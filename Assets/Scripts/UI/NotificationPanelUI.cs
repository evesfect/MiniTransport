using UnityEngine;
using System.Linq;

public class NotificationPanelUI : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject basicCardPrefab;
    public GameObject buyBusApprovalCardPrefab;
    public GameObject sellBusApprovalCardPrefab;

    private void OnEnable()
    {
        if (RequestManager.Instance != null)
        {
            RequestManager.Instance.OnRequestsUpdated += RefreshNotifications;
            RefreshNotifications();
        }
    }

    private void OnDisable()
    {
        if (RequestManager.Instance != null)
            RequestManager.Instance.OnRequestsUpdated -= RefreshNotifications;
    }

    private void RefreshNotifications()
    {
        foreach (Transform child in transform) Destroy(child.gameObject);

        PlayerRole myRole = RoleManager.Instance.GetMyRole();

        // Only show requests that aren't archived/read
        var activeNotifications = RequestManager.Instance.ActiveRequests
            .Where(r => r.State != RequestState.Read && (r.Requester == myRole || r.CurrentTarget == myRole))
            .ToList();

        foreach (var req in activeNotifications)
        {
            GameObject prefabToSpawn = basicCardPrefab; // Default

            // If I am the responder, and it requires a special approval UI:
            if (req.CurrentTarget == myRole && (req.State == RequestState.Active || req.State == RequestState.AwaitingGeneralManager))
            {
                if (req.Type == RequestType.BuyBus) prefabToSpawn = buyBusApprovalCardPrefab;
                else if (req.Type == RequestType.SellBus) prefabToSpawn = sellBusApprovalCardPrefab;
            }

            var cardObj = Instantiate(prefabToSpawn, transform);
            cardObj.transform.localScale = Vector3.one;

            // Initialize the specific script
            if (prefabToSpawn == basicCardPrefab) cardObj.GetComponent<BasicNotificationCardUI>().Setup(req, myRole);
            else if (prefabToSpawn == buyBusApprovalCardPrefab) cardObj.GetComponent<BuyBusApprovalCardUI>().Setup(req);
            else if (prefabToSpawn == sellBusApprovalCardPrefab) cardObj.GetComponent<SellBusApprovalCardUI>().Setup(req);
        }
    }
}