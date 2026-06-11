using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ClientDataCache", menuName = "Game/Data/ClientDataCache")]
public class ClientDataCache : ScriptableObject
{
    [SerializeField] private CompanyStatsData companyData;
    [SerializeField] private FleetStatsData fleetData;
    [SerializeField] private MaintenanceStatsData maintenanceData;

    [Header("End-of-game Reports")]
    [SerializeField] private GeneralReportData generalReport;
    [SerializeField] private OperationsReportData operationsReport;
    [SerializeField] private MaintenanceReportData maintenanceReport;
    [SerializeField] private HrReportData hrReport;
    [SerializeField] private FinanceReportData financeReport;
    [SerializeField] private VendorReportData vendorReport;

    public event Action<CompanyStatsData> OnCompanyDataUpdated;
    public event Action<FleetStatsData> OnFleetDataUpdated;
    public event Action<MaintenanceStatsData> OnMaintenanceDataUpdated;

    public event Action<GeneralReportData> OnGeneralReportUpdated;
    public event Action<OperationsReportData> OnOperationsReportUpdated;
    public event Action<MaintenanceReportData> OnMaintenanceReportUpdated;
    public event Action<HrReportData> OnHrReportUpdated;
    public event Action<FinanceReportData> OnFinanceReportUpdated;
    public event Action<VendorReportData> OnVendorReportUpdated;

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

    // --- Report setters/getters ---

    public void SetGeneralReport(GeneralReportData data)
    {
        generalReport = data;
        OnGeneralReportUpdated?.Invoke(generalReport);
    }

    public void SetOperationsReport(OperationsReportData data)
    {
        operationsReport = data;
        OnOperationsReportUpdated?.Invoke(operationsReport);
    }

    public void SetMaintenanceReport(MaintenanceReportData data)
    {
        maintenanceReport = data;
        OnMaintenanceReportUpdated?.Invoke(maintenanceReport);
    }

    public void SetHrReport(HrReportData data)
    {
        hrReport = data;
        OnHrReportUpdated?.Invoke(hrReport);
    }

    public void SetFinanceReport(FinanceReportData data)
    {
        financeReport = data;
        OnFinanceReportUpdated?.Invoke(financeReport);
    }

    public void SetVendorReport(VendorReportData data)
    {
        vendorReport = data;
        OnVendorReportUpdated?.Invoke(vendorReport);
    }

    public GeneralReportData GetGeneralReport() => generalReport;
    public OperationsReportData GetOperationsReport() => operationsReport;
    public MaintenanceReportData GetMaintenanceReport() => maintenanceReport;
    public HrReportData GetHrReport() => hrReport;
    public FinanceReportData GetFinanceReport() => financeReport;
    public VendorReportData GetVendorReport() => vendorReport;
}
