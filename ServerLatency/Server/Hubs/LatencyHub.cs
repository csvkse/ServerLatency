using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ServerLatency.Common.Models;
using ServerLatency.Server.Services;
using System.Collections.Concurrent;

namespace ServerLatency.Server.Hubs;

public class LatencyHub : Hub
{
    private readonly string? _accessKey;

    public LatencyHub(IConfiguration configuration)
    {
        _accessKey = configuration["AccessKey"];
    }

    public async Task Join(string key)
    {
        bool isAuth = !string.IsNullOrEmpty(_accessKey) && key == _accessKey;
        
        if (isAuth)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Authorized");
        }
        else
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Public");
        }

        ServerState.FrontendViewers.TryAdd(Context.ConnectionId, true);
        if (ServerState.FrontendViewers.Count == 1)
        {
            await BroadcastTargetsAsync();
        }
    }

    public async Task JoinPingNode(string name, string key, string? nodeIp)
    {
        if (!string.IsNullOrEmpty(_accessKey) && key != _accessKey) 
        {
            Console.WriteLine($"[Auth Failed] Node '{name}' provided wrong access key.");
            Context.Abort();
            return;
        }

        var ip = string.IsNullOrWhiteSpace(nodeIp) ? Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() : nodeIp;
        ip = NormalizeIp(ip);

        if (string.IsNullOrWhiteSpace(ip))
        {
            ip = "Unknown";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine($"[Validation Failed] Node connection attempt with empty name.");
            Context.Abort();
            return;
        }

        var node = new LatencyNode 
        { 
            ConnectionId = Context.ConnectionId, 
            Name = name, 
            Ip = ip,
            ConnectedAt = DateTime.Now,
            LastSeen = DateTime.Now
        };

        ServerState.OnlineNodes.AddOrUpdate(Context.ConnectionId, node, (k, v) => node);
        await Groups.AddToGroupAsync(Context.ConnectionId, "PingNodes");
        
        await Clients.Caller.SendAsync("Welcome", $"Connected as {name} ({ip})");
        await BroadcastTargetsAsync();
    }

    public async Task Report(List<LatencyReportItem> items, string key)
    {
        if (!string.IsNullOrEmpty(_accessKey) && key != _accessKey) return;
        
        var remoteIp = NormalizeIp(Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString());
        ServerLatency.Common.Models.LatencyNode? currentNode = null;

        foreach (var node in ServerState.OnlineNodes.Values)
        {
            if (node.ConnectionId == Context.ConnectionId || node.Ip == remoteIp)
            {
                node.LastSeen = DateTime.Now;
                if (node.ConnectionId == Context.ConnectionId) currentNode = node;
            }
        }

        if (items == null || items.Count == 0) return;
        
        var fallbackIp = currentNode?.Ip ?? remoteIp;

        foreach (var item in items)
        {
            item.SourceIp = NormalizeIp(string.IsNullOrEmpty(item.SourceIp) ? fallbackIp : item.SourceIp);
            item.TargetIp = NormalizeIp(item.TargetIp);

            if (string.IsNullOrEmpty(item.SourceIp) || string.IsNullOrEmpty(item.TargetIp)) continue;

            var realKey = ServerState.GenerateMatrixKey(item.SourceIp, item.TargetIp);
            var record = new LatencyRecord
            {
                SourceIp = item.SourceIp,
                SourceName = item.SourceName,
                TargetIp = item.TargetIp,
                LatencyMs = item.LatencyMs,
                IsSuccess = item.IsSuccess,
                Timestamp = DateTime.Now
            };

            var history = ServerState.LatencyHistory.GetOrAdd(realKey, _ => new ConcurrentQueue<LatencyRecord>());
            history.Enqueue(record);
            
            var threshold = DateTime.Now.AddMinutes(-35);
            while (history.TryPeek(out var old) && old.Timestamp < threshold)
            {
                history.TryDequeue(out _);
            }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        bool wasPingNode = ServerState.OnlineNodes.TryRemove(Context.ConnectionId, out _);
        bool wasViewer = ServerState.FrontendViewers.TryRemove(Context.ConnectionId, out _);

        if (wasPingNode)
        {
            if (ServerState.FrontendViewers.Count > 0)
                await BroadcastTargetsAsync();
        }
        else if (wasViewer)
        {
            if (ServerState.FrontendViewers.Count == 0)
                await BroadcastTargetsAsync();
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task BroadcastTargetsAsync()
    {
        if (ServerState.FrontendViewers.Count == 0)
        {
            await Clients.Group("PingNodes").SendAsync("UpdateTargets", new List<string>());
            return;
        }

        var targetIps = ServerState.OnlineNodes.Values
            .Select(n => n.Ip)
            .Where(ip => !string.IsNullOrEmpty(ip))
            .Distinct()
            .ToList();

        await Clients.Group("PingNodes").SendAsync("UpdateTargets", targetIps);
    }

    private static string NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return string.Empty;
        if (System.Net.IPAddress.TryParse(ip, out var address))
        {
            if (address.IsIPv4MappedToIPv6) return address.MapToIPv4().ToString();
            return address.ToString();
        }
        return ip;
    }
}
