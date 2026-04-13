using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ClientDataCache", menuName = "Game/Data/ClientDataCache")]
public class ClientDataCache : ScriptableObject
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

    public CompanyStatsData GetCompanyData() => companyData;
    public FleetStatsData GetFleetData() => fleetData;
    public MaintenanceStatsData GetMaintenanceData() => maintenanceData;
}