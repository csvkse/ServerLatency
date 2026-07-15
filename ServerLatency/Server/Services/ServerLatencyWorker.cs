using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using ServerLatency.Common.Models;

namespace ServerLatency.Server.Services;

/// <summary>
/// 后台服务：自动获取本机公网IP并加入Ping列表，执行对其他节点的Ping任务
/// (仅在 Server 模式下启用，作为 Master 节点参与监控)
/// </summary>
public class ServerLatencyWorker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ServerLatencyWorker> _logger;
    private readonly string? _userConfiguredIpApi;
    private readonly string[] _fallbackApis = new[]
    {
        "https://api.ipify.org",
        "https://api.ip.sb/ip",
        "https://icanhazip.com",
        "https://ifconfig.me/ip"
    };

    private string? _myPublicIp;
    private readonly string _serverName;
    private const string SERVER_NODE_ID = "SERVER_MASTER";

    public ServerLatencyWorker(IHttpClientFactory httpClientFactory, ILogger<ServerLatencyWorker> logger, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _userConfiguredIpApi = config["ServerConfig:PublicIpApi"];
        
        string? nameFromConfig = config["ServerConfig:ServerName"] ?? config["ClientConfig:ClientName"] ?? config["ClientName"];
        _serverName = string.IsNullOrWhiteSpace(nameFromConfig) ? "Master-Server" : nameFromConfig;
        
        string? ipFromConfig = config["ClientConfig:ClientIp"] ?? config["ClientIp"];
        if (!string.IsNullOrWhiteSpace(ipFromConfig))
        {
            _myPublicIp = ipFromConfig;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. 初始化：获取本机公网 IP
        while (_myPublicIp == null && !stoppingToken.IsCancellationRequested)
        {
            var apisToTry = new List<string>();
            if (!string.IsNullOrWhiteSpace(_userConfiguredIpApi))
            {
                apisToTry.Add(_userConfiguredIpApi);
            }
            apisToTry.AddRange(_fallbackApis);

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            foreach (var api in apisToTry)
            {
                if (stoppingToken.IsCancellationRequested) break;
                try
                {
                    _myPublicIp = await client.GetStringAsync(api, stoppingToken);
                    _myPublicIp = _myPublicIp?.Trim();
                    
                    if (!string.IsNullOrEmpty(_myPublicIp))
                    {
                        _logger.LogInformation($"Server IP resolved via {api}: {_myPublicIp}");
                        break; // 成功获取，跳出循环
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to resolve server IP via {api}: {ex.Message}. Trying next...");
                }
            }

            if (string.IsNullOrEmpty(_myPublicIp))
            {
                _logger.LogWarning("All API endpoints failed to resolve server IP. Retrying in 10s...");
                await Task.Delay(10000, stoppingToken);
            }
        }

        if (stoppingToken.IsCancellationRequested || string.IsNullOrEmpty(_myPublicIp)) return;

        // 2. 注册自己为节点 (仅执行一次)
        var serverNode = new LatencyNode
        {
            ConnectionId = SERVER_NODE_ID,
            Name = _serverName,
            Ip = _myPublicIp!,
            ConnectedAt = DateTime.Now,
            LastSeen = DateTime.Now
        };
        
        ServerState.OnlineNodes[SERVER_NODE_ID] = serverNode;

        // 3. 循环 Ping 其他节点
        using var pinger = new Ping();
        while (!stoppingToken.IsCancellationRequested)
        {
            try 
            {
                // 更新自己的心跳频率提升到 10秒 (与 Delay 一致)
                if (ServerState.OnlineNodes.TryGetValue(SERVER_NODE_ID, out var self))
                {
                    self.LastSeen = DateTime.Now;
                }

                var targets = ServerState.OnlineNodes.Values
                    .Where(n => n.ConnectionId != SERVER_NODE_ID && !string.IsNullOrEmpty(n.Ip))
                    .ToList();

                foreach (var target in targets)
                {
                    long latency = 0; bool success = false;
                    try
                    {
                        var reply = await pinger.SendPingAsync(target.Ip, 2000);
                        if (reply.Status == IPStatus.Success)
                        {
                            latency = reply.RoundtripTime;
                            success = true;
                        }
                    }
                    catch { }

                    var key = ServerState.GenerateMatrixKey(_myPublicIp!, target.Ip);
                    var record = new LatencyRecord
                    {
                        SourceName = _serverName,
                        SourceIp = _myPublicIp!,
                        TargetIp = target.Ip,
                        LatencyMs = latency,
                        IsSuccess = success,
                        Timestamp = DateTime.Now
                    };
                    
                    var history = ServerState.LatencyHistory.GetOrAdd(key, _ => new ConcurrentQueue<LatencyRecord>());
                    history.Enqueue(record);

                    var threshold = DateTime.Now.AddMinutes(-35);
                    while (history.TryPeek(out var old) && old.Timestamp < threshold) history.TryDequeue(out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ServerLatencyWorker loop");
            }

            await Task.Delay(10000, stoppingToken); // 每10秒执行一次
        }
    }
}
