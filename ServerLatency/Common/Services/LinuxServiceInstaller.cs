using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ServerLatency.Common.Services;

public static class LinuxServiceInstaller
{
    private const string ServiceName = "serverlatency";

    public static async Task HandleInstallAsync(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("This command is only supported on Linux.");
            return;
        }

        try
        {
            Console.WriteLine($"Installing {ServiceName} service...");

            // 1. Get paths
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                using var process = Process.GetCurrentProcess();
                exePath = process.MainModule?.FileName;
            }
            
            if (string.IsNullOrEmpty(exePath))
            {
                Console.WriteLine("Error: Could not determine executable path.");
                return;
            }
            
            // Ensure absolute path
            exePath = Path.GetFullPath(exePath);
            var workingDir = Path.GetDirectoryName(exePath) ?? "/";

            // 2. Filter args for the service command
            // Remove installation flags
            var installFlags = new[] { "-i", "--install", "-u", "--uninstall" };
            var serviceArgs = args.Where(a => !installFlags.Contains(a)).ToArray();
            var serviceArgsString = string.Join(" ", serviceArgs);

            // 3. Create Unit file content
            var unitContent = $"""
[Unit]
Description=Server Latency Service ({ServiceName})
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory={workingDir}
ExecStart={exePath} {serviceArgsString}
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier={ServiceName}

[Install]
WantedBy=multi-user.target
""";

            var serviceFilePath = $"/etc/systemd/system/{ServiceName}.service";

            Console.WriteLine($"Writing service file to {serviceFilePath}...");
            await File.WriteAllTextAsync(serviceFilePath, unitContent);

            Console.WriteLine("Reloading systemd daemon...");
            await RunCommandAsync("systemctl", "daemon-reload");

            Console.WriteLine($"Enabling {ServiceName} service...");
            await RunCommandAsync("systemctl", $"enable {ServiceName}");

            Console.WriteLine($"Starting {ServiceName} service...");
            await RunCommandAsync("systemctl", $"start {ServiceName}");

            Console.WriteLine("Installation completed successfully. Service status:");
            await RunCommandAsync("systemctl", $"status {ServiceName} --no-pager");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Installation failed: {ex.Message}");
            if (ex is UnauthorizedAccessException || ex.Message.Contains("Access to the path"))
            {
                Console.WriteLine("Hint: Please run as root (sudo).");
            }
        }
    }

    public static async Task HandleUninstallAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("This command is only supported on Linux.");
            return;
        }

        try
        {
            Console.WriteLine($"Uninstalling {ServiceName} service...");

            Console.WriteLine("Stopping service...");
            // Ignore errors if service doesn't exist
            await RunCommandAsync("systemctl", $"stop {ServiceName}", ignoreErrors: true);

            Console.WriteLine("Disabling service...");
            await RunCommandAsync("systemctl", $"disable {ServiceName}", ignoreErrors: true);

            var serviceFilePath = $"/etc/systemd/system/{ServiceName}.service";
            if (File.Exists(serviceFilePath))
            {
                Console.WriteLine($"Removing service file {serviceFilePath}...");
                File.Delete(serviceFilePath);
            }
            else
            {
                Console.WriteLine("Service file not found.");
            }

            Console.WriteLine("Reloading systemd daemon...");
            await RunCommandAsync("systemctl", "daemon-reload");

            Console.WriteLine("Uninstallation completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Uninstallation failed: {ex.Message}");
            if (ex is UnauthorizedAccessException)
            {
                Console.WriteLine("Hint: Please run as root (sudo).");
            }
        }
    }

    private static async Task RunCommandAsync(string command, string args, bool ignoreErrors = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        string output = await outputTask;
        string error = await errorTask;

        if (!string.IsNullOrWhiteSpace(output))
        {
            Console.WriteLine(output.Trim());
        }

        if (process.ExitCode != 0 && !ignoreErrors)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine($"Error: {error.Trim()}");
            }
            throw new Exception($"Command '{command} {args}' exited with code {process.ExitCode}");
        }
    }
}
