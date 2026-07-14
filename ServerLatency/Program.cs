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

namespace ServerLatency;

class Program
{
    static async Task Main(string[] args)
    {
        var helpArgs = new[] { "-h", "--h", "-help", "--help", "/?", "/h", "/help" };
        if (args.Any(a => helpArgs.Contains(a.ToLower())))
        {
            Console.WriteLine("ServerLatency - Global Latency Matrix Monitor");
            Console.WriteLine("Usage: ./ServerLatency [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -m, --Mode <mode>          Operation mode: 'Client' or 'Server'. Default is 'Client'.");
            Console.WriteLine("  -p, --Port <port>          (Server only) Port to listen on. Default: 15002.");
            Console.WriteLine("  -k, --Key <key>            Access key to authenticate Client/Server and access UI.");
            Console.WriteLine("  -n, --Name <name>          Node display name in the matrix.");
            Console.WriteLine("  -u, --ServerUrl <url>      (Client only) URL of the Control Plane API.");
            Console.WriteLine("  -ip, --Ip <ip>             (Client only) Override reported public IP.");
            Console.WriteLine("  -api, --Api <url>          (Server only) Custom API to fetch Server public IP.");
            Console.WriteLine();
            Console.WriteLine("Service Management (Linux only):");
            Console.WriteLine("  --install                  Install as a systemd service.");
            Console.WriteLine("  --uninstall                Uninstall the systemd service.");
            Console.WriteLine();
            return;
        }

        // Check for Service Install/Uninstall commands (Linux only)
        if (args.Any(a => a == "--install" || a == "-i"))
        {
            await LinuxServiceInstaller.HandleInstallAsync(args);
            return;
        }
        if (args.Any(a => a == "--uninstall"))
        {
            await LinuxServiceInstaller.HandleUninstallAsync();
            return;
        }

        // Setup configuration
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        
        var switchMappings = new Dictionary<string, string>()
        {
            { "-m", "Mode" },
            { "--Mode", "Mode" },
            { "-p", "ServerConfig:Port" },
            { "--Port", "ServerConfig:Port" },
            { "-k", "AccessKey" },
            { "--Key", "AccessKey" },
            { "-n", "ClientName" },
            { "--Name", "ClientName" },
            { "-u", "ServerUrl" },
            { "--ServerUrl", "ServerUrl" },
            { "-ip", "ClientIp" },
            { "--Ip", "ClientIp" },
            { "-api", "ServerConfig:PublicIpApi" },
            { "--Api", "ServerConfig:PublicIpApi" }
        };

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args, switchMappings);

        var config = builder.Build();

        string mode = config["Mode"] ?? "Client";
        
        Console.WriteLine($"ServerLatency Starting in [{mode.ToUpper()}] mode...");
        Console.WriteLine($"Environment: {env}");

        if (mode.Equals("Server", StringComparison.OrdinalIgnoreCase))
        {
            await ServerLatency.Server.ServerApp.RunServerAsync(args, config);
        }
        else
        {
            await ServerLatency.Client.ClientApp.RunClientAsync(args, config);
        }
    }
}
