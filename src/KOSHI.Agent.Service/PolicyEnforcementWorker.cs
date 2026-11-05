using System.Diagnostics;
using System.Runtime.Versioning;
using KOSHI.Agent.Core;
using KOSHI.Contracts;
using Microsoft.Win32;

namespace KOSHI.Agent.Service;

/// <summary>
/// Fetches the effective policy and enforces blocked applications at the SYSTEM service level:
/// 1) AppLocker XML policy (on Enterprise/Education SKUs)
/// 2) Windows Image File Execution Options (IFEO) kernel-level launch prevention
/// 3) Fast 300ms SYSTEM process killer (catches already-running, portable, or renamed executables via FileVersionInfo)
/// 4) Website blocking with timed exceptions and VPN blocking
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PolicyEnforcementWorker(ILogger<PolicyEnforcementWorker> logger, PolicyState state) : BackgroundService
{
    private const string IfeoBaseKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string KoshiMarkerValue = "KoshiBlocked";

    private (Guid? Id, int? Version, PolicyModeDto Mode, bool Approved)? _lastApplied;
    private readonly WebFilterEnforcer _webFilter = new(logger);
    private readonly VpnBlocker _vpn = new(logger);
    private readonly NetworkAdapterGuard _wifi = new(logger);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        state.LoadCache();
        return Task.WhenAll(PolicySyncLoop(stoppingToken), ProcessEnforcementLoop(stoppingToken), NetworkControlsLoop(stoppingToken), ShellLoop(stoppingToken));
    }

    /// <summary>Re-evaluated every 3 s so timed site permissions expire on time even while offline.</summary>
    private async Task NetworkControlsLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (state.Current is { } policy)
                {
                    var now = DateTime.UtcNow;
                    var sites = policy.BlockedSites ?? [];
                    var blocked = sites.Where(s => !AllowanceActive(s.AllowedUntil, now)).Select(s => s.Domain).ToList();
                    var allowed = sites.Where(s => AllowanceActive(s.AllowedUntil, now)).Select(s => s.Domain).ToList();
                    if (policy.VpnBlocked) blocked.AddRange(VpnCatalog.Domains);
                    _webFilter.Apply(blocked, allowed, policy.VpnBlocked);
                    _vpn.Tick(policy.VpnBlocked);
                    _wifi.Tick();
                }
                else
                {
                    _wifi.Tick();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Network controls tick failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task PolicySyncLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (DeviceRejectedException ex)
            {
                logger.LogWarning("Device rejected by server: {Reason}", ex.Reason);
                if (ex.Reason is ServerRejection.Revoked or ServerRejection.Invalid)
                    AgentStore.TryDelete();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Server unreachable; will retry");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Policy enforcement cycle failed");
            }
            await state.WaitForRefreshAsync(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var config = AgentStore.LoadConfig();
        var credential = AgentStore.LoadCredential();
        if (config is { LicenseExpiresAt: { } exp } && exp <= DateTime.UtcNow)
        {
            AgentStore.TryDelete();
            return;
        }
        if (config is null || credential is null) return;

        using var api = new KoshiApiClient(config.ServerUrl, credential);
        var policy = await api.GetPolicyAsync(ct);
        state.Set(policy);

        ApplyIfeoBlocks(policy);

        var key = (policy.PolicyId, policy.Version, policy.Mode, policy.EnforcementApproved);
        if (_lastApplied == key) return;

        var result = AppLockerAdapter.Apply(policy);
        logger.LogInformation("Policy v{Version} via {Adapter}: {Status}", policy.Version, result.Adapter, result.Status);
        if (policy.PolicyId is { } id && policy.Version is { } version)
        {
            await api.ReportPolicyResultAsync(new PolicyResultReportRequest
            {
                PolicyId = id, PolicyVersion = version, Adapter = result.Adapter, Mode = policy.Mode, Status = result.Status, Message = result.Message,
            }, ct);
        }
        _lastApplied = key;
    }

    private async Task ShellLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { ShellGuard.Tick(harden: false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Shell guard tick failed");
            }
            await Task.Delay(80, ct);
        }
    }

    private async Task ProcessEnforcementLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var blocked = (state.Current?.Rules ?? [])
                    .Where(r => r.Restriction == ApplicationRestrictionDto.Block)
                    .Select(r => NormalizeName(r.ProcessName))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var n in ShellGuard.Names) blocked.Add(n);

                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.SessionId == 0) continue; // never touch session 0 services
                        if (SelfHeal.IsHealProcess(UninstallGuard.ReadCommandLine(p.Id))) continue;
                        if (IsBlockedProcess(p, blocked))
                        {
                            try { p.Kill(entireProcessTree: true); }
                            catch { p.Kill(); }
                        }
                    }
                    catch
                    {
                        // Process already exited
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Process enforcement tick failed");
            }
            await Task.Delay(300, ct);
        }
    }

    private static bool IsBlockedProcess(Process p, HashSet<string> blockedNames)
    {
        if (blockedNames.Contains(NormalizeName(p.ProcessName))) return true;
        try
        {
            var vi = p.MainModule?.FileVersionInfo;
            if (vi is null) return false;
            if (!string.IsNullOrWhiteSpace(vi.OriginalFilename) && blockedNames.Contains(NormalizeName(vi.OriginalFilename)))
                return true;
            if (!string.IsNullOrWhiteSpace(vi.InternalName) && blockedNames.Contains(NormalizeName(vi.InternalName)))
                return true;
        }
        catch
        {
            // Ignore access errors when reading module info
        }
        return false;
    }

    /// <summary>
    /// Uses Windows Image File Execution Options (IFEO) so the OS kernel refuses to start blocked executables
    /// before a single instruction runs. Cleans up any previous KOSHI IFEO entries when rules change.
    /// </summary>
    private void ApplyIfeoBlocks(AgentPolicyDto policy)
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(IfeoBaseKey, writable: true);
            if (baseKey is null) return;

            var desiredExeNames = (policy.Rules ?? [])
                .Where(r => r.Restriction == ApplicationRestrictionDto.Block)
                .Select(r => EnsureExeExtension(r.ProcessName))
                .Where(n => !string.IsNullOrEmpty(n)
                            && !n.StartsWith("KOSHI", StringComparison.OrdinalIgnoreCase)
                            && !SelfHeal.HealImages.Contains(n, StringComparer.OrdinalIgnoreCase)
                            && !ShellGuard.IsName(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var exe in ShellGuard.IfeoImages) desiredExeNames.Add(exe);

            foreach (var image in SelfHeal.ProtectedImages)
            {
                using var mine = baseKey.OpenSubKey(image, writable: true);
                if (mine is null) continue;
                mine.DeleteValue("Debugger", throwOnMissingValue: false);
                mine.DeleteValue("KoshiBlocked", throwOnMissingValue: false);
            }

            // Remove old KOSHI blocks that are no longer in the blocked list
            foreach (var subName in baseKey.GetSubKeyNames())
            {
                if (desiredExeNames.Contains(subName)) continue;
                using var sub = baseKey.OpenSubKey(subName, writable: true);
                if (sub?.GetValue(KoshiMarkerValue) is not null)
                {
                    sub.DeleteValue("Debugger", throwOnMissingValue: false);
                    sub.DeleteValue(KoshiMarkerValue, throwOnMissingValue: false);
                }
            }

            // Apply current blocks
            foreach (var exe in desiredExeNames)
            {
                using var sub = baseKey.CreateSubKey(exe, writable: true);
                if (sub is null) continue;
                sub.SetValue(KoshiMarkerValue, 1, RegistryValueKind.DWord);
                sub.SetValue("Debugger", "systray.exe", RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "IFEO registry update failed");
        }
    }

    private static string NormalizeName(string name) =>
        Path.GetFileNameWithoutExtension(name.Trim());

    private static string EnsureExeExtension(string name)
    {
        var n = Path.GetFileName(name.Trim());
        if (string.IsNullOrEmpty(n)) return string.Empty;
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n : $"{n}.exe";
    }

    /// <summary>Timed site permission is still valid (UTC, even if JSON deserialized the timestamp as Unspecified).</summary>
    private static bool AllowanceActive(DateTime? until, DateTime utcNow)
    {
        if (until is not { } t) return false;
        if (t.Kind == DateTimeKind.Unspecified) t = DateTime.SpecifyKind(t, DateTimeKind.Utc);
        else t = t.ToUniversalTime();
        return t > utcNow;
    }
}
