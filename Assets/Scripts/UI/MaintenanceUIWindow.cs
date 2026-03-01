using UnityEngine;
using TMPro;
using System.Text;

public class MaintenanceUIWindow : MonoBehaviour
{
    [SerializeField] private UIDataCache dataCache;
    [SerializeField] private GameObject masterBrokerObject;
    private IUIDataProvider dataProvider;

    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI thresholdsText;
    [SerializeField] private TextMeshProUGUI busListText; // A tall text box for the list

    private void Awake()
    {
        if (masterBrokerObject != null)
            dataProvider = masterBrokerObject.GetComponent<IUIDataProvider>();
    }

    private void OnEnable()
    {
        dataCache.OnMaintenanceDataUpdated += UpdateUI;
        dataProvider?.RegisterInterest(UIDataType.MaintenanceStats);
        UpdateUI(dataCache.GetMaintenanceData());
    }

    private void OnDisable()
    {
        dataCache.OnMaintenanceDataUpdated -= UpdateUI;
        dataProvider?.UnregisterInterest(UIDataType.MaintenanceStats);
    }

    private void UpdateUI(MaintenanceStatsData data)
    {
        thresholdsText.text = $"Operational Threshold: {data.operationalThreshold}%\nBreakdown Threshold: {data.breakdownThreshold}%";

        if (data.busHealthList == null || data.busHealthList.Count == 0)
        {
            busListText.text = "No buses in fleet.";
            return;
        }

        // Build a formatted string of all bus durabilities
        StringBuilder sb = new StringBuilder();
        foreach (var bus in data.busHealthList)
        {
            // Optional: Add a warning tag if below operational threshold
            string warning = bus.durability < data.operationalThreshold ? " <color=red>[NEEDS REPAIR]</color>" : "";
            
            sb.AppendLine($"Bus {bus.busID}: {bus.durability:F1}%{warning}");
        }

        busListText.text = sb.ToString();
    }
}