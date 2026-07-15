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

namespace ServerLatency.Server;

public static class ServerApp
{

    public static async Task RunServerAsync(string[] args, IConfigurationRoot mainConfig)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        
        // 1. Sync config
        if (!string.IsNullOrEmpty(mainConfig["ServerConfig:Port"]))
            builder.Configuration["ServerConfig:Port"] = mainConfig["ServerConfig:Port"];
        if (!string.IsNullOrEmpty(mainConfig["AccessKey"]))
            builder.Configuration["AccessKey"] = mainConfig["AccessKey"];

        // 2. Positional Args
        var positionals = args.Where(a => !a.StartsWith("-")).ToList();
        if (positionals.Count > 0 && int.TryParse(positionals[0], out var p))
        {
            builder.Configuration["ServerConfig:Port"] = p.ToString();
            if (positionals.Count > 1) builder.Configuration["AccessKey"] = positionals[1];
        }

        // 3. Configure Kestrel
        var port = builder.Configuration.GetValue<int>("ServerConfig:Port", 15002);
        builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

        // 4. Services
        builder.Services.AddHttpClient();
        builder.Services.AddHostedService<ServerLatencyWorker>();
        
        builder.Services.AddSignalR()
            .AddJsonProtocol(options => 
            {
                options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
            });
        builder.Services.AddHostedService<MatrixSignalRBroadcaster>();
        
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
        });

        builder.Services.AddOpenApi();

        var app = builder.Build();
        
        // Serve embedded static files
        var assembly = typeof(ServerApp).Assembly;
        var embeddedProvider = new Microsoft.Extensions.FileProviders.ManifestEmbeddedFileProvider(assembly, "Server/wwwroot");
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = embeddedProvider,
            RequestPath = "",
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                ctx.Context.Response.Headers.Append("Expires", "-1");
            }
        });

        // 启用反向代理 IP 解析 (适配 Nginx 等)
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
        };
        forwardedHeadersOptions.KnownNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedHeadersOptions);

        if (app.Environment.IsDevelopment())
        {
             app.MapOpenApi();
        }

        app.MapHub<ServerLatency.Server.Hubs.LatencyHub>("/latencyHub");

        var accessKey = builder.Configuration["AccessKey"];
        var api = app.MapGroup("/api/ServerLatency");

        // Auth
        api.MapPost("/Auth", (string key) => 
        {
            if (!string.IsNullOrEmpty(accessKey) && key != accessKey) return Results.Unauthorized();
            return Results.Ok("Authorized");
        });



        // Matrix
        api.MapGet("/Matrix", (string? key) =>
        {
            var isAuth = !string.IsNullOrEmpty(accessKey) && key == accessKey;
            var now = DateTime.Now;
            var t1m = now.AddMinutes(-1);
            var t10m = now.AddMinutes(-10);
            var t30m = now.AddMinutes(-30);
            var realTimeThreshold = now.AddSeconds(-60);

            // Masking helper
            static string MaskIp(string? ip)
            {
                if (string.IsNullOrEmpty(ip)) return "";
                if (ip.Contains(':')) return "IPv6-Masked";
                var parts = ip.Split('.');
                if (parts.Length == 4) return $"{parts[0]}.{parts[1]}.*.*";
                return "Masked";
            }

            // Calculation helper
            static double CalculateLoss(IEnumerable<LatencyRecord> records)
            {
                var list = records.ToList();
                if (list.Count == 0) return 0;
                return (double)list.Count(r => !r.IsSuccess) / list.Count;
            }

            var activeNodes = ServerState.OnlineNodes.Values
                .Select(n => new OnlineNodeInfo
                {
                    Name = n.Name,
                    Ip = isAuth ? n.Ip : MaskIp(n.Ip),
                    DisplayIp = isAuth ? n.Ip : null,
                    LastSeen = n.LastSeen
                })
                .ToList();

            var matrix = new List<LatencyMatrixItem>();
            foreach (var entry in ServerState.LatencyHistory)
            {
                var historyList = entry.Value.ToList();
                var lastRecord = historyList.LastOrDefault();

                if (lastRecord == null || lastRecord.Timestamp < realTimeThreshold) continue;

                matrix.Add(new LatencyMatrixItem
                {
                    SourceName = lastRecord.SourceName,
                    SourceIp = isAuth ? lastRecord.SourceIp : MaskIp(lastRecord.SourceIp),
                    TargetIp = isAuth ? lastRecord.TargetIp : MaskIp(lastRecord.TargetIp),
                    LatencyMs = lastRecord.LatencyMs,
                    IsSuccess = lastRecord.IsSuccess,
                    LossRate1m = CalculateLoss(historyList.Where(r => r.Timestamp >= t1m)),
                    LossRate10m = CalculateLoss(historyList.Where(r => r.Timestamp >= t10m)),
                    LossRate30m = CalculateLoss(historyList.Where(r => r.Timestamp >= t30m)),
                    Timestamp = lastRecord.Timestamp
                });
            }

            return Results.Ok(new MatrixResponse
            {
                ServerTime = now,
                OnlineNodes = activeNodes,
                Matrix = matrix.OrderBy(m => m.SourceIp).ThenBy(m => m.TargetIp).ToList(),
                IsAuth = isAuth
            });
        });

        // Default to index.html
        app.MapGet("/", () => Results.Redirect("/index.html"));
        
        Console.WriteLine($"* Server Port : {port}");
        
        string? nameFromConfig = builder.Configuration["ServerConfig:ServerName"] ?? builder.Configuration["NodeConfig:NodeName"] ?? builder.Configuration["NodeName"] ?? builder.Configuration["ClientName"];
        string serverName = string.IsNullOrWhiteSpace(nameFromConfig) ? "Master-Server" : nameFromConfig;
        Console.WriteLine($"* Server Name : {serverName}");

        var accessKey = builder.Configuration["AccessKey"];
        if (!string.IsNullOrEmpty(accessKey))
        {
            Console.WriteLine($"* Access Key  : {accessKey.Substring(0, Math.Min(4, accessKey.Length))}***");
        }
        else
        {
            Console.WriteLine($"* Access Key  : [DISABLED]");
        }
        Console.WriteLine("----------------------------------------------------------");
        
        await app.RunAsync();
    }


}
