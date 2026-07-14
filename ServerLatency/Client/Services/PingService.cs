using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using ServerLatency.Common.Models;

namespace ServerLatency.Client.Services;

public class PingService
{
    private readonly string _sourceName;

    public PingService(string sourceName)
    {
        _sourceName = sourceName;
    }

    public async Task<List<LatencyReportItem>> PingTargetsAsync(IEnumerable<string> targets)
    {
        var results = new List<LatencyReportItem>();
        using var ping = new Ping();

        foreach (var rawTarget in targets)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawTarget)) continue;

                // Normalize IP: Handle IPv4-mapped IPv6 (e.g., ::ffff:192.168.1.1)
                string target = rawTarget;
                if (IPAddress.TryParse(rawTarget, out var ip))
                {
                    if (ip.IsIPv4MappedToIPv6)
                    {
                        target = ip.MapToIPv4().ToString();
                    }
                    else 
                    {
                         target = ip.ToString();
                    }
                }

                var reply = await ping.SendPingAsync(target, 2000);
                if (reply.Status == IPStatus.Success)
                {
                    results.Add(new LatencyReportItem
                    {
                        SourceName = _sourceName,
                        SourceIp = null,
                        TargetIp = target,
                        LatencyMs = reply.RoundtripTime,
                        IsSuccess = true
                    });
                    Console.WriteLine($"[Ping] {target} -> {reply.RoundtripTime}ms");
                }
                else
                {
                    results.Add(new LatencyReportItem
                    {
                        SourceName = _sourceName,
                        SourceIp = null,
                        TargetIp = target,
                        LatencyMs = 0,
                        IsSuccess = false
                    });
                    Console.WriteLine($"[Ping] {target} -> {reply.Status} (Failed)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ping Error] {rawTarget}: {ex.Message}");
                results.Add(new LatencyReportItem
                {
                    SourceName = _sourceName,
                    SourceIp = null,
                    TargetIp = rawTarget,
                    LatencyMs = 0,
                    IsSuccess = false
                });
            }
        }

        return results;
    }
}
