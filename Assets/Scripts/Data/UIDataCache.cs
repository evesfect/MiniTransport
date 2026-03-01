using System;
using UnityEngine;

[CreateAssetMenu(fileName = "UIDataCache", menuName = "Game/UI/UIDataCache")]
public class UIDataCache : ScriptableObject
{
    [SerializeField] private CompanyStatsData companyData;
    [SerializeField] private FleetStatsData fleetData;
    [SerializeField] private MaintenanceStatsData maintenanceData;

    public event Action<CompanyStatsData> OnCompanyDataUpdated;
    public event Action<FleetStatsData> OnFleetDataUpdated;
    public event Action<MaintenanceStatsData> OnMaintenanceDataUpdated;

    public void SetCompanyData(CompanyStatsData data)
    {
        companyData = data;
        OnCompanyDataUpdated?.Invoke(companyData);
    }

    public void SetFleetData(FleetStatsData data)
    {
        fleetData = data;
        OnFleetDataUpdated?.Invoke(fleetData);
    }
    
    public void SetMaintenanceData(MaintenanceStatsData data)
    {
        maintenanceData = data;
        OnMaintenanceDataUpdated?.Invoke(maintenanceData);
    }
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            OnCompanyDataUpdated?.Invoke(companyData);
            OnFleetDataUpdated?.Invoke(fleetData);
        }
    }

    public CompanyStatsData GetCompanyData() => companyData;
    public FleetStatsData GetFleetData() => fleetData;
    public MaintenanceStatsData GetMaintenanceData() => maintenanceData;
}