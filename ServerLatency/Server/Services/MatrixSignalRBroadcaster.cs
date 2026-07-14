using Microsoft.AspNetCore.SignalR;
using ServerLatency.Server.Hubs;
using ServerLatency.Common.Models;

namespace ServerLatency.Server.Services;

public class MatrixSignalRBroadcaster : BackgroundService
{
    private readonly IHubContext<LatencyHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MatrixSignalRBroadcaster> _logger;

    public MatrixSignalRBroadcaster(IHubContext<LatencyHub> hubContext, IConfiguration configuration, ILogger<MatrixSignalRBroadcaster> logger)
    {
        _hubContext = hubContext;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var t1m = now.AddMinutes(-1);
                var t10m = now.AddMinutes(-10);
                var t30m = now.AddMinutes(-30);
                var realTimeThreshold = now.AddSeconds(-60);

                string MaskIp(string? ip)
                {
                    if (string.IsNullOrEmpty(ip)) return "";
                    if (ip.Contains(':')) return "IPv6-Masked";
                    var parts = ip.Split('.');
                    if (parts.Length == 4) return $"{parts[0]}.{parts[1]}.*.*";
                    return "Masked";
                }

                double CalculateLoss(IEnumerable<LatencyRecord> records)
                {
                    var list = records.ToList();
                    if (list.Count == 0) return 0;
                    return (double)list.Count(r => !r.IsSuccess) / list.Count;
                }

                // Authorized
                var activeNodesAuth = ServerState.OnlineNodes.Values
                    .Select(n => new OnlineNodeInfo
                    {
                        Name = n.Name,
                        Ip = n.Ip,
                        DisplayIp = n.Ip,
                        LastSeen = n.LastSeen
                    }).ToList();

                // Public
                var activeNodesPublic = ServerState.OnlineNodes.Values
                    .Select(n => new OnlineNodeInfo
                    {
                        Name = n.Name,
                        Ip = MaskIp(n.Ip),
                        DisplayIp = null,
                        LastSeen = n.LastSeen
                    }).ToList();

                var matrixAuth = new List<LatencyMatrixItem>();
                var matrixPublic = new List<LatencyMatrixItem>();

                foreach (var entry in ServerState.LatencyHistory)
                {
                    var historyList = entry.Value.ToList();
                    var lastRecord = historyList.LastOrDefault();

                    if (lastRecord == null || lastRecord.Timestamp < realTimeThreshold) continue;

                    var loss1m = CalculateLoss(historyList.Where(r => r.Timestamp >= t1m));
                    var loss10m = CalculateLoss(historyList.Where(r => r.Timestamp >= t10m));
                    var loss30m = CalculateLoss(historyList.Where(r => r.Timestamp >= t30m));

                    var history5m = historyList.Where(r => r.Timestamp >= now.AddMinutes(-5))
                        .Select(r => new LatencyHistoryPoint
                        {
                            LatencyMs = r.LatencyMs,
                            IsSuccess = r.IsSuccess,
                            Timestamp = r.Timestamp
                        }).ToList();

                    matrixAuth.Add(new LatencyMatrixItem
                    {
                        SourceName = lastRecord.SourceName,
                        SourceIp = lastRecord.SourceIp,
                        TargetIp = lastRecord.TargetIp,
                        LatencyMs = lastRecord.LatencyMs,
                        IsSuccess = lastRecord.IsSuccess,
                        LossRate1m = loss1m,
                        LossRate10m = loss10m,
                        LossRate30m = loss30m,
                        Timestamp = lastRecord.Timestamp,
                        History = history5m
                    });

                    matrixPublic.Add(new LatencyMatrixItem
                    {
                        SourceName = lastRecord.SourceName,
                        SourceIp = MaskIp(lastRecord.SourceIp),
                        TargetIp = MaskIp(lastRecord.TargetIp),
                        LatencyMs = lastRecord.LatencyMs,
                        IsSuccess = lastRecord.IsSuccess,
                        LossRate1m = loss1m,
                        LossRate10m = loss10m,
                        LossRate30m = loss30m,
                        Timestamp = lastRecord.Timestamp,
                        History = history5m
                    });
                }

                var responseAuth = new MatrixResponse
                {
                    ServerTime = now,
                    OnlineNodes = activeNodesAuth,
                    Matrix = matrixAuth.OrderBy(m => m.SourceIp).ThenBy(m => m.TargetIp).ToList(),
                    IsAuth = true
                };

                var responsePublic = new MatrixResponse
                {
                    ServerTime = now,
                    OnlineNodes = activeNodesPublic,
                    Matrix = matrixPublic.OrderBy(m => m.SourceIp).ThenBy(m => m.TargetIp).ToList(),
                    IsAuth = false
                };

                await _hubContext.Clients.Group("Authorized").SendAsync("ReceiveMatrixUpdate", responseAuth, cancellationToken: stoppingToken);
                await _hubContext.Clients.Group("Public").SendAsync("ReceiveMatrixUpdate", responsePublic, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting Matrix via SignalR");
            }

            await Task.Delay(2000, stoppingToken);
        }
    }
}
