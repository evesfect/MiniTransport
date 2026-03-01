using UnityEngine;
using TMPro;

public class CompanyUIWindow : MonoBehaviour
{
    [SerializeField] private UIDataCache dataCache;
    [SerializeField] private GameObject brokerObject; // Drag your MasterUIDataBroker here
    private IUIDataProvider dataProvider;

    [SerializeField] private TextMeshProUGUI balanceText;

    private void Awake()
    {
        if (brokerObject != null)
            dataProvider = brokerObject.GetComponent<IUIDataProvider>();
    }

    private void OnEnable()
    {
        // 1. Subscribe to the cache
        dataCache.OnCompanyDataUpdated += RefreshUI;
        
        // 2. Ask the Broker to start the data flow
        dataProvider?.RegisterInterest(UIDataType.CompanyStats);
        
        // 3. Populate immediately
        RefreshUI(dataCache.GetCompanyData());
    }

    private void OnDisable()
    {
        // 1. Unsubscribe from cache
        dataCache.OnCompanyDataUpdated -= RefreshUI;
        
        // 2. Tell the Broker we no longer need the data
        dataProvider?.UnregisterInterest(UIDataType.CompanyStats);
    }

    private void RefreshUI(CompanyStatsData data)
    {
        balanceText.text = $"Balance: ${data.currentBalance:N2}";
    }
}