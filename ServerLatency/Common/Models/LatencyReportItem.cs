namespace ServerLatency.Common.Models;

public class LatencyReportItem
{
    public string? SourceName { get; set; }
    public string? SourceIp { get; set; }
    public string? TargetIp { get; set; }
    public double LatencyMs { get; set; }
    public bool IsSuccess { get; set; }
}
