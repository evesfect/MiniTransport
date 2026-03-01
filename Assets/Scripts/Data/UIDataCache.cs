using System;
using UnityEngine;

[CreateAssetMenu(fileName = "UIDataCache", menuName = "Game/UI/UIDataCache")]
public class UIDataCache : ScriptableObject
{
    [SerializeField] private CompanyStatsData companyData;
    [SerializeField] private FleetStatsData fleetData;

    public event Action<CompanyStatsData> OnCompanyDataUpdated;
    public event Action<FleetStatsData> OnFleetDataUpdated;

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
}