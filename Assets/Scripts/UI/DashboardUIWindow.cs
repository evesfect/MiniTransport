using UnityEngine;
using TMPro;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class DashboardUIWindow : MonoBehaviour
{
    [SerializeField] private ClientDataCache dataCache;
    [SerializeField] private GameObject localBrokerObject;
    private ILocalDataProvider dataProvider;

    [Header("Company Text")]
    [SerializeField] private TextMeshProUGUI balanceText;
    [SerializeField] private TextMeshProUGUI transferTripsText;

    [Header("Fleet & Maintenance Text")]
    [SerializeField] private TextMeshProUGUI busesText;

    private FleetStatsData _lastFleetData;
    private MaintenanceStatsData _lastMaintenanceData;

    private void Awake()
    {
        if (localBrokerObject != null)
            dataProvider = localBrokerObject.GetComponent<ILocalDataProvider>();
    }

    private void OnEnable()
    {
        dataCache.OnCompanyDataUpdated += UpdateCompanyUI;
        dataCache.OnFleetDataUpdated += UpdateFleetUI;
        dataCache.OnMaintenanceDataUpdated += UpdateMaintenanceUI;

        // Default subscriptions on open (Notice we do NOT subscribe to the Ledger here)
        SubscribeToSpecific(SyncDataType.CompanyStats);
        SubscribeToSpecific(SyncDataType.FleetStats);
        SubscribeToSpecific(SyncDataType.MaintenanceStats);

        UpdateCompanyUI(dataCache.GetCompanyData());
        UpdateFleetUI(dataCache.GetFleetData());
        UpdateMaintenanceUI(dataCache.GetMaintenanceData());
    }

    private void OnDisable()
    {
        dataCache.OnCompanyDataUpdated -= UpdateCompanyUI;
        dataCache.OnFleetDataUpdated -= UpdateFleetUI;
        dataCache.OnMaintenanceDataUpdated -= UpdateMaintenanceUI;
        
        UnsubscribeToAll();
    }

    // --- Public API for the Custom Inspector Buttons ---

    public void SubscribeToSpecific(SyncDataType type)
    {
        if (Application.isPlaying && dataProvider != null)
        {
            Debug.Log($"<color=cyan>[UI Window]</color> Requesting subscription for: {type}");
            dataProvider.RegisterInterest(type);
        }
            
    }

    public void UnsubscribeFromSpecific(SyncDataType type)
    {
        if (Application.isPlaying && dataProvider != null)
            dataProvider.UnregisterInterest(type);
    }

    public void UnsubscribeToAll()
    {
        if (Application.isPlaying && dataProvider != null)
            dataProvider.UnregisterInterest(SyncDataType.CompanyStats | SyncDataType.FleetStats | SyncDataType.MaintenanceStats | SyncDataType.CompanyLedger);
    }

    // --- Visual Updates ---

    private void UpdateCompanyUI(CompanyStatsData data)
    {
        if (balanceText != null)
            balanceText.text = $"Balance: ${data.currentBalance:N2}";

        if (transferTripsText != null)
            transferTripsText.text = $"Transfer Trips: {data.transferTripCount}";
    }

    private void UpdateFleetUI(FleetStatsData data)
    {
        _lastFleetData = data;
        RefreshCombinedFleetText();
    }

    private void UpdateMaintenanceUI(MaintenanceStatsData data)
    {
        _lastMaintenanceData = data;
        RefreshCombinedFleetText();
    }

    private void RefreshCombinedFleetText()
    {
        if (busesText == null) return;

        StringBuilder sb = new StringBuilder();
        
        sb.AppendLine($"Total Buses: {_lastFleetData.totalBuses}");
        sb.AppendLine($"Needs Repair: {_lastFleetData.lowDurabilityBuses}");
        sb.AppendLine(); 
        
        if (_lastMaintenanceData.busHealthList != null && _lastMaintenanceData.busHealthList.Count > 0)
        {
            foreach (var bus in _lastMaintenanceData.busHealthList)
            {
                string warning = bus.durability < _lastMaintenanceData.operationalThreshold ? " (Repair Needed)" : "";
                sb.AppendLine($"Bus {bus.busID}: {bus.durability:F1}%{warning}");
            }
        }
        else
        {
            sb.AppendLine("No buses in fleet.");
        }

        busesText.text = sb.ToString();
    }
}

// --- CUSTOM INSPECTOR BUTTONS ---
#if UNITY_EDITOR
[CustomEditor(typeof(DashboardUIWindow))]
public class DashboardUIWindowEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        DashboardUIWindow script = (DashboardUIWindow)target;

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("Network Data Subscriptions (Live Testing)", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to test targeted network subscriptions.", MessageType.Info);
            return;
        }

        // Loop through every enum flag automatically
        foreach (SyncDataType type in System.Enum.GetValues(typeof(SyncDataType)))
        {
            if (type == SyncDataType.None) continue;

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(type.ToString(), GUILayout.Width(150));
            
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Subscribe", GUILayout.Width(80))) 
                script.SubscribeToSpecific(type);
            
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("Unsubscribe", GUILayout.Width(90))) 
                script.UnsubscribeFromSpecific(type);
            
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }
    }
}
#endif