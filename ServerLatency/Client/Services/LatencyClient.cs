using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using ServerLatency.Common.Models;
using ServerLatency.Common.Serialization;

namespace ServerLatency.Client.Services;

public class LatencyClient
{
    private readonly string _baseUrl;
    private readonly string _clientName;
    private readonly string _accessKey;
    private readonly string? _clientIp;
    private readonly PingService _pingService;
    private HubConnection? _connection;
    private List<string> _lastTargets = new();
    private readonly object _targetLock = new();

    public LatencyClient(string baseUrl, string clientName, string accessKey, string? clientIp = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _clientName = clientName;
        _accessKey = accessKey;
        _clientIp = clientIp;
        _pingService = new PingService(clientName);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var hubUrl = $"{_baseUrl}/latencyHub";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .AddJsonProtocol(options => 
            {
                options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
            })
            .Build();

        _connection.On<string>("Welcome", message =>
        {
            Console.WriteLine($"[Server] {message}");
        });

        _connection.On<List<string>>("UpdateTargets", async targets =>
        {
            Console.WriteLine($"[Event] Received targets update: {string.Join(", ", targets)}");
            lock (_targetLock)
            {
                _lastTargets = targets ?? new List<string>();
            }

            if (_lastTargets.Count > 0)
            {
                var results = await _pingService.PingTargetsAsync(_lastTargets);
                if (results.Count > 0)
                {
                    await ReportResultsAsync(results, cancellationToken);
                }
            }
        });

        _connection.Closed += async (error) =>
        {
            Console.WriteLine($"[Connection Closed] {error?.Message}");
            await Task.Delay(5000, cancellationToken);
        };

        // Start periodic ping
        _ = Task.Run(() => PeriodicPingLoopAsync(cancellationToken), cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_connection.State == HubConnectionState.Disconnected)
                {
                    Console.WriteLine($"Connecting to {hubUrl} via SignalR...");
                    await _connection.StartAsync(cancellationToken);
                    Console.WriteLine("Connected to SignalR Hub.");
                    
                    await _connection.InvokeAsync("JoinPingNode", _clientName, _accessKey, _clientIp, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Connection Error] {ex.Message}");
            }

            await Task.Delay(5000, cancellationToken);
        }
    }

    private async Task PeriodicPingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            List<string> targets;
            lock (_targetLock)
            {
                targets = new List<string>(_lastTargets);
            }

            if (targets.Count > 0 && _connection?.State == HubConnectionState.Connected)
            {
                try
                {
                    var results = await _pingService.PingTargetsAsync(targets);
                    if (results.Count > 0)
                    {
                        await ReportResultsAsync(results, ct);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Periodic Ping Error] {ex.Message}");
                }
            }

            await Task.Delay(10000, ct);
        }
    }

    private async Task ReportResultsAsync(List<LatencyReportItem> results, CancellationToken ct)
    {
        if (_connection?.State != HubConnectionState.Connected) return;

        try
        {
            if (!string.IsNullOrEmpty(_clientIp))
            {
                foreach (var item in results)
                {
                    item.SourceIp = _clientIp;
                }
            }

            await _connection.InvokeAsync("Report", results, _accessKey, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Report Error] {ex.Message}");
        }
    }
}
