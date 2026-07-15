using System.Text.Json.Serialization;
using ServerLatency.Common.Models;

namespace ServerLatency.Common.Serialization;

[JsonSerializable(typeof(MatrixResponse))]
[JsonSerializable(typeof(ReportResponse))]
[JsonSerializable(typeof(List<OnlineNodeInfo>))]
[JsonSerializable(typeof(List<LatencyRecord>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<LatencyReportItem>))]
[JsonSerializable(typeof(LatencyMatrixItem))]
[JsonSerializable(typeof(List<LatencyMatrixItem>))]
[JsonSerializable(typeof(LatencyHistoryPoint))]
[JsonSerializable(typeof(List<LatencyHistoryPoint>))]
[JsonSerializable(typeof(SseMessage))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(object[]))]
public partial class AppJsonContext : JsonSerializerContext
{
}
