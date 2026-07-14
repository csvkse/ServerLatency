using System.Text.Json.Serialization;

namespace ServerLatency.Common.Models;

public class MatrixResponse
{
    public DateTime ServerTime { get; set; }
    public bool IsAuth { get; set; }
    public List<OnlineNodeInfo> OnlineNodes { get; set; } = new();
    public List<LatencyMatrixItem> Matrix { get; set; } = new();
}

public class LatencyMatrixItem
{
    public string? SourceName { get; set; }
    public string? SourceIp { get; set; }
    public string? TargetIp { get; set; }
    public double LatencyMs { get; set; }
    public bool IsSuccess { get; set; }
    public double LossRate1m { get; set; }
    public double LossRate10m { get; set; }
    public double LossRate30m { get; set; }
    public DateTime Timestamp { get; set; }
    public List<LatencyHistoryPoint>? History { get; set; }
}

public class LatencyHistoryPoint
{
    public double LatencyMs { get; set; }
    public bool IsSuccess { get; set; }
    public DateTime Timestamp { get; set; }
}

public class OnlineNodeInfo
{
    public string? Name { get; set; }
    public string? Ip { get; set; } // Can be Real IP or Masked ID
    public string? DisplayIp { get; set; } // Real IP if Authorized, null if not
    public DateTime LastSeen { get; set; }
}

public class ReportResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }
}
