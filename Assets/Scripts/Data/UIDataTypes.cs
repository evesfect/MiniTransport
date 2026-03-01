using System;

[Flags]
public enum UIDataType
{
    None = 0,
    CompanyStats = 1 << 0,
    FleetStats = 1 << 1,
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