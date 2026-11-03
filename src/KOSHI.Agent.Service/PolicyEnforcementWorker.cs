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
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PolicyEnforcementWorker(ILogger<PolicyEnforcementWorker> logger) : BackgroundService
{
    private const string IfeoBaseKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string KoshiMarkerValue = "KoshiBlocked";

    private (Guid? Id, int? Version, PolicyModeDto Mode, bool Approved)? _lastApplied;
    private volatile AgentPolicyDto? _currentPolicy;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(PolicySyncLoop(stoppingToken), ProcessEnforcementLoop(stoppingToken));

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
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Server unreachable; will retry");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Policy enforcement cycle failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var config = AgentStore.LoadConfig();
        var credential = AgentStore.LoadCredential();
        if (config is null || credential is null) return;

        using var api = new KoshiApiClient(config.ServerUrl, credential);
        var policy = await api.GetPolicyAsync(ct);
        _currentPolicy = policy;

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

    private async Task ProcessEnforcementLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var policy = _currentPolicy;
                if (policy?.Rules is { Count: > 0 } rules)
                {
                    var blocked = rules
                        .Where(r => r.Restriction == ApplicationRestrictionDto.Block)
                        .Select(r => NormalizeName(r.ProcessName))
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (blocked.Count > 0)
                    {
                        foreach (var p in Process.GetProcesses())
                        {
                            try
                            {
                                if (p.SessionId == 0) continue; // never touch session 0 services
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
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
}
