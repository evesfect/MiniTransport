using System;
using System.Collections.Generic;

[Flags]
public enum UIDataType
{
    None = 0,
    CompanyStats = 1 << 0,
    FleetStats = 1 << 1,
    MaintenanceStats = 1 << 2,
}

[Serializable]
public struct CompanyStatsData
{
    public float currentBalance;
    public int totalTransactions;
}

[Serializable]
public struct FleetStatsData
{
    public int totalBuses;
    public int lowDurabilityBuses;
}

[Serializable]
public struct BusHealthData
{
    public string busID;
    public float durability;
}

[Serializable]
public struct MaintenanceStatsData
{
    public float operationalThreshold;
    public float breakdownThreshold;
    public List<BusHealthData> busHealthList;
}