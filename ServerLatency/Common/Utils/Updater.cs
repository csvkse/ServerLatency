using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ServerLatency.Common.Utils;

public static class Updater
{
    private const string RepoOwner = "csvkse";
    private const string RepoName = "ServerLatency";
    private const string GitHubApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    public static async Task UpdateAsync()
    {
        Console.WriteLine("Checking for updates...");

        try
        {
            var os = GetOSIdentifier();
            var arch = GetArchIdentifier();
            var targetAssetNamePrefix = $"ServerLatency-{os}-{arch}";
            Console.WriteLine($"Detected environment: {os}-{arch}");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ServerLatency", "1.0"));

            var response = await httpClient.GetAsync(GitHubApiUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tagName = root.GetProperty("tag_name").GetString();
            Console.WriteLine($"Latest release: {tagName}");

            var assets = root.GetProperty("assets").EnumerateArray();
            string? downloadUrl = null;
            string? assetName = null;

            foreach (var asset in assets)
            {
                var name = asset.GetProperty("name").GetString();
                if (name != null && name.StartsWith(targetAssetNamePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    assetName = name;
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(assetName))
            {
                Console.WriteLine($"No suitable asset found for {targetAssetNamePrefix}.");
                return;
            }

            Console.WriteLine($"Downloading {assetName}...");
            var tempFile = Path.Combine(Path.GetTempPath(), assetName);
            
            var downloadResponse = await httpClient.GetAsync(downloadUrl);
            downloadResponse.EnsureSuccessStatusCode();
            
            await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await downloadResponse.Content.CopyToAsync(fs);
            }

            Console.WriteLine("Download complete. Extracting...");
            var extractPath = Path.Combine(Path.GetTempPath(), $"ServerLatency_Update_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractPath);

            if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                await using var fs = File.OpenRead(tempFile);
                await using var gz = new GZipStream(fs, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gz, extractPath, overwriteFiles: true);
            }
            else if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(tempFile, extractPath, overwriteFiles: true);
            }
            else
            {
                Console.WriteLine($"Unsupported archive format: {assetName}");
                return;
            }

            // Find the extracted executable
            string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(currentExePath))
            {
                Console.WriteLine("Could not determine current executable path.");
                return;
            }

            var exeName = Path.GetFileName(currentExePath);
            var newExePath = Path.Combine(extractPath, exeName);
            if (!File.Exists(newExePath))
            {
                // Fallback: looking for exactly "ServerLatency" or "ServerLatency.exe"
                newExePath = Path.Combine(extractPath, "ServerLatency");
                if (!File.Exists(newExePath))
                {
                    newExePath = Path.Combine(extractPath, "ServerLatency.exe");
                }
            }

            if (!File.Exists(newExePath))
            {
                Console.WriteLine($"Could not find executable {exeName} in the downloaded archive.");
                return;
            }

            Console.WriteLine("Replacing current executable...");
            var oldExePath = currentExePath + ".old";
            
            if (File.Exists(oldExePath))
            {
                try { File.Delete(oldExePath); } catch { /* Ignore */ }
            }

            File.Move(currentExePath, oldExePath);
            File.Move(newExePath, currentExePath);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                File.SetUnixFileMode(currentExePath, 
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            Console.WriteLine($"Successfully updated to {tagName}. Please restart the application.");

            // Cleanup
            try
            {
                File.Delete(tempFile);
                Directory.Delete(extractPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update failed: {ex.Message}");
        }
    }

    private static string GetOSIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "osx";
        return "unknown";
    }

    private static string GetArchIdentifier()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLower()
        };
    }
}
