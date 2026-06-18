/// <summary>
/// Shared formatting helpers for the end-of-game report panels. Negative values are treated as
/// "not tracked" stubs (KPIManager uses -1 for KPIs with no data source yet) and render as "N/A".
/// </summary>
public static class ReportFormat
{
    public static string Pct(float v) => v < 0f ? "N/A" : $"%{v:F0}";
    public static string Minutes(float v) => v < 0f ? "N/A" : $"{v:F0} min";
    public static string Hours(float v) => v < 0f ? "N/A" : $"{v:F1} h";
    public static string Money(float v) => $"{v:N0}";
    public static string Score(float v) => v < 0f ? "N/A" : $"{v:F0}";
    public static string Count(int v) => v < 0 ? "N/A" : $"{v}";
    public static string PerDay(float v) => v < 0f ? "N/A" : $"{v:F1}/day";
}
