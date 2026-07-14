using System.Collections.Concurrent;
using ServerLatency.Common.Models;

namespace ServerLatency.Server.Services;

public static class ServerState
{
    // 1. 在线节点：ConnectionId -> NodeInfo
    public static readonly ConcurrentDictionary<string, LatencyNode> OnlineNodes = new();
    
    // 3. 延迟历史记录：SourceIp_TargetIp -> Queue of recent results
    public static readonly ConcurrentDictionary<string, ConcurrentQueue<LatencyRecord>> LatencyHistory = new();

    // 4. 前端网页在线连接数
    public static readonly ConcurrentDictionary<string, bool> FrontendViewers = new();

    public static string GenerateMatrixKey(string source, string target) => $"{source}_{target}";
}
