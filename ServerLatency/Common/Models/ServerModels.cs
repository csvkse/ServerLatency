using System;

namespace ServerLatency.Common.Models;

public class LatencyNode
{
    public string ConnectionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public DateTime ConnectedAt { get; set; }
    public DateTime LastSeen { get; set; }
}

public class SseMessage
{
    public string Event { get; set; }
    public object Data { get; set; }

    public SseMessage(string evt, object data)
    {
        Event = evt;
        Data = data;
    }
}

public class LatencyRecord : LatencyReportItem
{
    public DateTime Timestamp { get; set; }
}
