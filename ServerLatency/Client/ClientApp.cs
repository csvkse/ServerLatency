using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using ServerLatency.Common.Models;
using ServerLatency.Common.Serialization;
using ServerLatency.Common.Services;
using ServerLatency.Server.Services;
using ServerLatency.Client.Services;

namespace ServerLatency.Client;

public static class ClientApp
{
    public static async Task RunClientAsync(string[] args, IConfigurationRoot config)
    {
        string baseUrl = config.GetValue<string>("NodeConfig:ServerUrl", "http://localhost:15002");
        string nodeName = config.GetValue<string>("NodeConfig:NodeName", $"Node_{Environment.MachineName}");
        string accessKey = config.GetValue<string>("AccessKey", "");
        
        // Argument Parsing Strategy:
        // Filter out flags (-k, --Key) and their values to find true "positional" arguments.
        // This stops "Client" (from -m Client) being mistaken for a NodeName.
        
        var positionals = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("-"))
            {
                // Skip the flag
                // Check if it's a known switch that takes a value (assuming all our switches take values for simplicity, except maybe help)
                // In our mappings all take values.
                i++; // Skip the value too
            }
            else
            {
                positionals.Add(args[i]);
            }
        }

        // Apply positional overrides (url, key, name)
        if (positionals.Count > 0) baseUrl = positionals[0];
        if (positionals.Count > 1) accessKey = positionals[1]; // 顺序调整：现在第2位是 Key
        if (positionals.Count > 2) nodeName = positionals[2]; // 顺序调整：现在第3位是 Name

        string nodeIp = config.GetValue<string>("NodeConfig:NodeIp", "");

        Console.WriteLine($"* Server      : {baseUrl}");
        Console.WriteLine($"* Node Name   : {nodeName}");
        if (!string.IsNullOrEmpty(nodeIp)) Console.WriteLine($"* Node IP     : {nodeIp} (Override)");
        if(!string.IsNullOrEmpty(accessKey)) Console.WriteLine($"* Access Key  : {accessKey.Substring(0, Math.Min(4, accessKey.Length))}***");
        Console.WriteLine("----------------------------------------------------------");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("Stopping...");
            cts.Cancel();
            e.Cancel = true;
        };

        var client = new LatencyClient(baseUrl, nodeName, accessKey, nodeIp);
        await client.StartAsync(cts.Token);
    }
}
