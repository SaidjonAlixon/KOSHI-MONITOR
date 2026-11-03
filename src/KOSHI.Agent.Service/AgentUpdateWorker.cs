using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using KOSHI.Agent.Core;

namespace KOSHI.Agent.Service;

file sealed record ReleaseInfo(string Version, string BuildDate, string FileName, long Size, string Sha256);

/// <summary>
/// Checks the server for a newer agent build and, when the administrator has enabled auto-update,
/// downloads the signed-by-hash installer and runs it silently. The installer performs an in-place
/// upgrade that keeps the existing activation. Downgrades and tampered files are refused.
/// </summary>
public sealed class AgentUpdateWorker(ILogger<AgentUpdateWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(2);
    private string? _attemptedVersion;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // let the agent settle after boot
        using var timer = new PeriodicTimer(CheckInterval);
        do
        {
            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Update check failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        var config = AgentStore.LoadConfig();
        var credential = AgentStore.LoadCredential();
        if (config is null || credential is null) return;

        bool autoUpdate;
        using (var api = new KoshiApiClient(config.ServerUrl, credential))
        {
            autoUpdate = (await api.GetPolicyAsync(ct)).AutoUpdateEnabled;
        }
        if (!autoUpdate) return;

        var baseUri = new Uri(config.ServerUrl.TrimEnd('/') + "/");
        using var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(10) };
        var release = await http.GetFromJsonSafeAsync<ReleaseInfo>("downloads/latest.json", ct);
        if (release is null || string.IsNullOrWhiteSpace(release.Version)) return;

        if (!Version.TryParse(release.Version, out var latest) || !Version.TryParse(AgentInfo.Version, out var current)) return;
        if (latest <= current) return;
        if (_attemptedVersion == release.Version)
        {
            logger.LogWarning("Update to {Version} already attempted this run; not retrying until restart", release.Version);
            return;
        }
        _attemptedVersion = release.Version;

        logger.LogInformation("New agent version {Latest} available (current {Current}); downloading", latest, current);
        var temp = Path.Combine(Path.GetTempPath(), $"KOSHI-Agent-Setup-{release.Version}.exe");
        var fileName = Path.GetFileName(release.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;

        using (var resp = await http.GetAsync($"downloads/{Uri.EscapeDataString(fileName)}", HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(temp);
            await resp.Content.CopyToAsync(fs, ct);
        }

        var actual = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(temp), ct));
        if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Downloaded installer hash mismatch; discarding. expected {Expected} actual {Actual}", release.Sha256, actual);
            File.Delete(temp);
            return;
        }

        logger.LogInformation("Installing agent update {Version} silently", release.Version);
        // UseShellExecute detaches the installer so it survives the service restart it triggers.
        Process.Start(new ProcessStartInfo(temp, "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES") { UseShellExecute = true });
    }
}

file static class HttpJsonExtensions
{
    public static async Task<T?> GetFromJsonSafeAsync<T>(this HttpClient http, string path, CancellationToken ct)
    {
        using var resp = await http.GetAsync(path, ct);
        if (!resp.IsSuccessStatusCode) return default;
        var json = (await resp.Content.ReadAsStringAsync(ct)).TrimStart('\uFEFF');
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
