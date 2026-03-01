using UnityEngine;
using TMPro;

public class DashboardUIWindow : MonoBehaviour
{
    [SerializeField] private UIDataCache dataCache;
    [SerializeField] private GameObject masterBrokerObject;
    private IUIDataProvider dataProvider;

    [Header("Company Text")]
    [SerializeField] private TextMeshProUGUI balanceText;

    [Header("Fleet Text")]
    [SerializeField] private TextMeshProUGUI busesText;

    private void Awake()
    {
        if (masterBrokerObject != null)
            dataProvider = masterBrokerObject.GetComponent<IUIDataProvider>();
    }

    private void OnEnable()
    {
        // 1. Listen to the cache
        dataCache.OnCompanyDataUpdated += UpdateCompanyUI;
        dataCache.OnFleetDataUpdated += UpdateFleetUI;

        // 2. Tell the broker we want BOTH Company and Fleet data
        dataProvider?.RegisterInterest(UIDataType.CompanyStats | UIDataType.FleetStats);

        // 3. Populate immediately
        UpdateCompanyUI(dataCache.GetCompanyData());
        UpdateFleetUI(dataCache.GetFleetData());
    }

    private void OnDisable()
    {
        dataCache.OnCompanyDataUpdated -= UpdateCompanyUI;
        dataCache.OnFleetDataUpdated -= UpdateFleetUI;
        
        // Stop requesting data
        dataProvider?.UnregisterInterest(UIDataType.CompanyStats | UIDataType.FleetStats);
    }

    private void UpdateCompanyUI(CompanyStatsData data)
    {
        balanceText.text = $"Balance: ${data.currentBalance:N2}\nTransactions: {data.totalTransactions}";
    }

    private void UpdateFleetUI(FleetStatsData data)
    {
        // CHANGED: Now using lowDurabilityBuses instead of activeRoutes
        busesText.text = $"Total Buses: {data.totalBuses} | Needs Repair: {data.lowDurabilityBuses}";
    }
}