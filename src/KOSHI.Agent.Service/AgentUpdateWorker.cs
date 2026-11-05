using System.Diagnostics;
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
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(2);
    private string? _attemptedVersion;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
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
        // When the interactive tray agent is running, let the user see the update notification and click "Yangilash".
        if (Process.GetProcessesByName("KOSHI.Agent").Length > 0) return;

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
        var release = await http.GetFromJsonSafeAsync<ReleaseInfo>("api/agent/release/latest.json", ct)
                      ?? await http.GetFromJsonSafeAsync<ReleaseInfo>("downloads/latest.json", ct);
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
        UpgradeGate.Enter(TimeSpan.FromMinutes(15));
        SelfHeal.BeginUpgrade();
        AgentSelfUpdate.ReleaseStale(release.Version);
        string? temp = null;
        try
        {
            temp = await AgentSelfUpdate.DownloadToTempAsync(http, release.Version, release.FileName, release.Size, progress: null, ct);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Update download locked; will retry next cycle");
            AgentSelfUpdate.ReleaseStale(release.Version);
            _attemptedVersion = null;
            return;
        }

        if (!await AgentSelfUpdate.ShaMatchesAsync(temp, release.Sha256, ct))
        {
            logger.LogError("Downloaded installer hash mismatch; discarding {Path}", temp);
            try { File.Delete(temp); } catch (IOException) { }
            _attemptedVersion = null;
            return;
        }

        logger.LogInformation("Installing agent update {Version} silently", release.Version);
        UpgradeGate.Enter(TimeSpan.FromMinutes(15));
        SelfHeal.BeginUpgrade();
        AgentSelfUpdate.Launch(temp, alreadyElevated: true);
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
