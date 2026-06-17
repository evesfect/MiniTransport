using UnityEngine;
using System.Linq;

public class NotificationPanelUI : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject basicCardPrefab;
    public GameObject buyBusApprovalCardPrefab;
    public GameObject sellBusApprovalCardPrefab;
    public GameObject loanApprovalCardPrefab; // [NEW]

    private void OnEnable()
    {
        if (RequestManager.Instance != null)
        {
            RequestManager.Instance.OnRequestsUpdated += RefreshNotifications;
            RefreshNotifications();
        }
    }

    private void Start()
    {
        RefreshNotifications();
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

        var activeNotifications = RequestManager.Instance.ActiveRequests
            .Where(r => r.State != RequestState.Read && (r.Requester == myRole || r.CurrentTarget == myRole))
            .ToList();

        foreach (var req in activeNotifications)
        {
            GameObject prefabToSpawn = basicCardPrefab; 

            // Assign special cards
            if (req.CurrentTarget == myRole && (req.State == RequestState.Active || req.State == RequestState.AwaitingGeneralManager))
            {
                if (req.Type == RequestType.BuyBus) prefabToSpawn = buyBusApprovalCardPrefab;
                else if (req.Type == RequestType.SellBus) prefabToSpawn = sellBusApprovalCardPrefab;
                else if (req.Type == RequestType.TakeLoan) prefabToSpawn = loanApprovalCardPrefab; // [NEW]
            }

            var cardObj = Instantiate(prefabToSpawn, transform);
            cardObj.transform.localScale = Vector3.one;

            // Setup specific scripts
            if (prefabToSpawn == basicCardPrefab) cardObj.GetComponent<BasicNotificationCardUI>().Setup(req, myRole);
            else if (prefabToSpawn == buyBusApprovalCardPrefab) cardObj.GetComponent<BuyBusApprovalCardUI>().Setup(req);
            else if (prefabToSpawn == sellBusApprovalCardPrefab) cardObj.GetComponent<SellBusApprovalCardUI>().Setup(req);
            else if (prefabToSpawn == loanApprovalCardPrefab) cardObj.GetComponent<LoanApprovalCardUI>().Setup(req); // [NEW]
        }
    }
}