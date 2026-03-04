using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BloxManager.Services
{
    public interface IUpdateService
    {
        Task<(bool hasUpdate, string current, string latest, string downloadUrl)> CheckForUpdateAsync(string owner, string repo);
        Task<string?> DownloadLatestAsync(string downloadUrl, IProgress<double>? progress = null, string fileNameHint = "BloxManager_Update.exe");
    }
    
    public class UpdateService : IUpdateService
    {
        private readonly ILogger<UpdateService> _logger;
        public UpdateService(ILogger<UpdateService> logger)
        {
            _logger = logger;
        }

        public async Task<(bool hasUpdate, string current, string latest, string downloadUrl)> CheckForUpdateAsync(string owner, string repo)
        {
            try
            {
                var currentFile = Process.GetCurrentProcess().MainModule!.FileName;
                var fvi = FileVersionInfo.GetVersionInfo(currentFile);
                var current = fvi.FileVersion ?? "0.0.0";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BloxManager", current));
                var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
                var resp = await client.GetAsync(url);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Update check failed: {Status} {Body}", resp.StatusCode, body);
                    return (false, current, current, string.Empty);
                }
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var tag = root.GetProperty("tag_name").GetString() ?? "";
                string downloadUrl = "";
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                var latest = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
                var hasUpdate = CompareVersions(latest, current) > 0;
                return (hasUpdate, current, latest, downloadUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed update check");
                return (false, "0.0.0", "0.0.0", string.Empty);
            }
        }

        public async Task<string?> DownloadLatestAsync(string downloadUrl, IProgress<double>? progress = null, string fileNameHint = "BloxManager_Update.exe")
        {
            try
            {
                if (string.IsNullOrEmpty(downloadUrl)) return null;
                var targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(targetDir);
                var fileName = Path.GetFileName(new Uri(downloadUrl).AbsolutePath);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = fileNameHint;
                var targetPath = Path.Combine(targetDir, fileName);

                using var http = new HttpClient();
                using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                var totalRead = 0L;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes.HasValue)
                    {
                        var percentage = (double)totalRead / totalBytes.Value * 100;
                        progress?.Report(percentage);
                    }
                }

                return targetPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download update");
                return null;
            }
        }

        private static int CompareVersions(string a, string b)
        {
            try
            {
                var pa = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
                var pb = b.Split('.', StringSplitOptions.RemoveEmptyEntries);
                var n = Math.Max(pa.Length, pb.Length);
                for (int i = 0; i < n; i++)
                {
                    var ai = i < pa.Length && int.TryParse(pa[i], out var av) ? av : 0;
                    var bi = i < pb.Length && int.TryParse(pb[i], out var bv) ? bv : 0;
                    if (ai != bi) return ai.CompareTo(bi);
                }
            }
            catch { }
            return 0;
        }
    }
}
