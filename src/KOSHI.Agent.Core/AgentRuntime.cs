using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.Json;
using KOSHI.Agent.Core.Remote;
using KOSHI.Agent.Core.Windows;
using KOSHI.Contracts;
using Microsoft.Extensions.Logging;

namespace KOSHI.Agent.Core;

public enum AgentState { NotActivated, Connecting, Online, Offline, Blocked, Revoked }

public sealed record AvailableUpdateInfo(string Version, string BuildDate, string FileName, long Size, string Sha256);

/// <summary>Implemented by the tray app: shows the password dialog for locked apps and terminates refused/blocked apps.</summary>
public interface IAppLockHost
{
    /// <summary>Returns true if the target process currently has a visible, non-minimized window on screen.</summary>
    bool HasActiveVisibleWindow(string processName);

    /// <summary>Called when a password-protected app is running and not yet unlocked.
    /// Returns true to allow it, false to close it. Verify with <see cref="AppLock.Verify"/>.</summary>
    bool ChallengeAndEnforce(AgentApplicationRuleDto rule);

    /// <summary>Immediately closes all user-session instances of a blocked application.</summary>
    void TerminateApp(string processName);
}

public static class AppLock
{
    public static bool Verify(AgentApplicationRuleDto rule, string typedPassword) =>
        KOSHI.Contracts.AppLockPassword.Verify(rule.PasswordSalt, rule.PasswordHash, typedPassword);
}

public sealed record ChatMessage(Guid Id, bool FromAdmin, string? SenderName, string Text, DateTime CreatedAt, bool IsRead = false);

public sealed record AgentStatus(
    AgentState State,
    string? ServerUrl,
    string? DeviceName,
    bool MonitoringActive,
    string? PolicyName,
    int? PolicyVersion,
    int QueueLength,
    DateTime? LastSync,
    string MonitoringNotice,
    string SupportContact,
    bool RemoteChannelConnected,
    bool AutoUpdateEnabled,
    string? LocationInfo = null,
    DateTime? LicenseExpiresAt = null);

/// <summary>Runs inside the interactive user session: sampling, local queue, upload, heartbeat, policy, app enforcement and real-time chat/remote channel.</summary>
[SupportedOSPlatform("windows")]
public sealed class AgentRuntime : IAsyncDisposable
{
    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SplitOpenAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PolicyEvery = TimeSpan.FromSeconds(60);

    private readonly IActivitySource _source;
    private readonly IRemoteHost _remoteHost;
    private readonly IAppLockHost _appLockHost;
    private readonly ILocationProvider? _location;
    private readonly SemaphoreSlim _locateGate = new(1, 1);
    private IpLocation? _ipLocation;
    private DateTime _ipLocationAt = DateTime.MinValue;
    private readonly ILogger _logger;
    private readonly Dictionary<string, DateTime> _unlockedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _unlockHiddenPolls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lockCooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _seenMessages = [];
    private readonly List<ChatMessage> _chatHistory = [];
    private DateTime? _chatSince;
    private DateTime _lastTypingSent = DateTime.MinValue;
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private string? _notifiedUpdateVersion;
    private readonly ActivityTracker _tracker = new();
    private readonly OfflineQueue _queue = new(AgentPaths.QueueDir);
    private readonly Dictionary<string, double> _todayForeground = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _violationsReported = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;
    private KoshiApiClient? _api;
    private RemoteConnection? _remote;
    private AgentPolicyDto? _policy;
    private AgentConfig? _config;
    private string? _rejectedCredential;
    private TaskCompletionSource? _rejection;
    private bool _monitoringAllowed = true;
    private DateOnly _today;

    public AgentRuntime(IActivitySource source, IRemoteHost remoteHost, IAppLockHost appLockHost, ILogger logger, ILocationProvider? location = null)
    {
        _source = source;
        _remoteHost = remoteHost;
        _appLockHost = appLockHost;
        _location = location;
        _logger = logger;
        Status = new AgentStatus(AgentState.NotActivated, null, null, false, null, null, 0, null, DefaultNotice, "", false, true);
    }

    public const string DefaultNotice = "Aqlli monitoring. Ishonchli nazorat.";

    public AgentStatus Status { get; private set; }

    public IReadOnlyList<ChatMessage> ChatHistory
    {
        get { lock (_gate) return [.. _chatHistory]; }
    }

    public event Action<AgentStatus>? StatusChanged;

    /// <summary>Raised for each support chat message (from admin, and echoes of the agent's own sent messages).</summary>
    public event Action<ChatMessage>? ChatReceived;
    public event Action<ChatMessage>? ChatUpdated;
    public event Action<Guid>? ChatDeleted;
    public event Action<IReadOnlyList<Guid>>? ChatRead;
    public event Action? ChatCleared;

    /// <summary>Raised when an administrator is typing a message to this device.</summary>
    public event Action<string>? AdminTyping;

    /// <summary>Raised when a newer agent version is published on the server.</summary>
    public event Action<AvailableUpdateInfo>? UpdateAvailable;

    public AvailableUpdateInfo? AvailableUpdate { get; private set; }

    public async Task<bool> SendChatAsync(string text)
    {
        if (_api is null || string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var m = await _api.SendSupportAsync(text.Trim(), CancellationToken.None);
            EmitChat(new ChatMessage(m.Id, false, null, m.Text, m.CreatedAt));
            if (m.CreatedAt > (_chatSince ?? DateTime.MinValue)) _chatSince = m.CreatedAt;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Chat send failed");
            return false;
        }
    }

    public void NotifyTyping()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastTypingSent).TotalMilliseconds < 1200) return;
        _lastTypingSent = now;
        if (_remote?.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
            _ = _remote.SendTypingAsync();
        else if (_api is not null)
            _ = _api.SendSupportTypingAsync(CancellationToken.None);
    }

    public void MarkAdminMessagesRead()
    {
        if (_remote?.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
            _ = _remote.SendMarkReadAsync();
    }

    private void EmitChat(ChatMessage msg)
    {
        lock (_gate)
        {
            if (!_seenMessages.Add(msg.Id)) return;
            _chatHistory.Add(msg);
        }
        ChatReceived?.Invoke(msg);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _config = AgentStore.LoadConfig();
            var credential = AgentStore.LoadCredential();
            // The tray may lack rights to delete ProgramData files; the service removes them shortly after.
            if (credential is not null && credential == _rejectedCredential)
            {
                _config = null;
                credential = null;
            }
            if (_config is { LicenseExpiresAt: { } localExp } && localExp <= DateTime.UtcNow)
            {
                AgentStore.TryDelete();
                _config = null;
                credential = null;
            }
            if (_config is null || credential is null)
            {
                SetStatus(s => s with { State = AgentState.NotActivated, MonitoringActive = false, LicenseExpiresAt = null, DeviceName = null });
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                continue;
            }

            SetStatus(s => s with { State = AgentState.Connecting, ServerUrl = _config.ServerUrl, DeviceName = _config.DisplayName, LicenseExpiresAt = _config.LicenseExpiresAt });
            _api = new KoshiApiClient(_config.ServerUrl, credential);
            _remote = new RemoteConnection(_api.BaseAddress, credential, _remoteHost, _logger);
            _remote.ChatMessageReceived += m =>
            {
                var fromAdmin = string.Equals(m.Sender, "Admin", StringComparison.OrdinalIgnoreCase);
                EmitChat(new ChatMessage(m.Id, fromAdmin, m.SenderName, m.Text, m.CreatedAt, m.ReadAt != null));
                if (m.CreatedAt > (_chatSince ?? DateTime.MinValue)) _chatSince = m.CreatedAt;
            };
            _remote.ChatMessageUpdated += m =>
            {
                var fromAdmin = string.Equals(m.Sender, "Admin", StringComparison.OrdinalIgnoreCase);
                var updated = new ChatMessage(m.Id, fromAdmin, m.SenderName, m.Text, m.CreatedAt, m.ReadAt != null);
                lock (_gate)
                {
                    var idx = _chatHistory.FindIndex(x => x.Id == m.Id);
                    if (idx >= 0) _chatHistory[idx] = updated;
                }
                ChatUpdated?.Invoke(updated);
            };
            _remote.ChatMessageDeleted += id =>
            {
                lock (_gate)
                {
                    _seenMessages.Remove(id);
                    _chatHistory.RemoveAll(x => x.Id == id);
                }
                ChatDeleted?.Invoke(id);
            };
            _remote.ChatMessagesRead += ids =>
            {
                lock (_gate)
                {
                    var set = ids.ToHashSet();
                    for (var i = 0; i < _chatHistory.Count; i++)
                    {
                        if (set.Contains(_chatHistory[i].Id))
                            _chatHistory[i] = _chatHistory[i] with { IsRead = true };
                    }
                }
                ChatRead?.Invoke(ids);
            };
            _remote.ChatCleared += () =>
            {
                lock (_gate)
                {
                    _seenMessages.Clear();
                    _chatHistory.Clear();
                }
                ChatCleared?.Invoke();
            };
            _remote.ChatTypingReceived += name => AdminTyping?.Invoke(name);
            _ = _remote.StartAsync(ct);

            // One loop being rejected (device deleted, license expired) must stop all of them.
            using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var rejection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _rejection = rejection;
            Task[] loops = [SampleLoop(session.Token), EnforceRulesLoop(session.Token), SyncLoop(session.Token), HeartbeatLoop(session.Token), LocationLoop(session.Token)];
            try
            {
                var first = await Task.WhenAny([.. loops, rejection.Task]);
                await session.CancelAsync();
                await first;
            }
            catch (DeviceRejectedException ex)
            {
                _logger.LogWarning("Server rejected device: {Reason}", ex.Reason);
                if (ex.Reason is ServerRejection.Revoked or ServerRejection.Invalid)
                {
                    _rejectedCredential = credential;
                    AgentStore.TryDelete();
                    _config = null;
                    _policy = null;
                    lock (_gate) _unlockedApps.Clear();
                    SetStatus(s => s with { State = AgentState.NotActivated, MonitoringActive = false, LicenseExpiresAt = null, DeviceName = null });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent session stopped; reconnecting");
            }
            _rejection = null;
            await session.CancelAsync();
            try { await Task.WhenAll(loops); }
            catch { /* loops end with cancellation or the rejection handled above */ }
            if (_remote is not null)
            {
                try { await _remote.DisposeAsync(); } catch { /* best-effort */ }
                _remote = null;
            }
            if (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task SampleLoop(CancellationToken ct)
    {
        var lastFlush = DateTime.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            lock (_gate)
            {
                ForegroundInfo? fg = null;
                double idle = 0;
                try
                {
                    fg = _source.GetForeground();
                    idle = _source.GetIdleSeconds();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Sampling failed");
                }

                _tracker.Sample(fg, idle, now, _monitoringAllowed);
                if (_monitoringAllowed && fg is not null && idle < _tracker.Settings.IdleThresholdSeconds) CheckRules(fg.ProcessName, now);

                if (now - lastFlush >= FlushEvery)
                {
                    var (events, sessions) = _tracker.Drain(now, SplitOpenAfter);
                    _queue.Enqueue(events, sessions, now);
                    lastFlush = now;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(_policy?.SamplingIntervalSeconds ?? 3), ct);
        }
    }

    // Windows surfaces that expose the uninstall path; locked when protection is enabled.
    private static readonly string[] ProtectedProcessNames =
    [
        "SystemSettings", "SystemSettingsAdminFlows", "SystemSettingsBroker",
    ];

    private static readonly string[] BlockedShells =
    [
        "cmd", "powershell", "pwsh", "powershell_ise", "WindowsTerminal", "wt", "OpenConsole",
    ];

    /// <summary>Fast 350ms enforcement loop: closes blocked apps immediately and prompts for password-locked apps whenever they run.</summary>
    private async Task EnforceRulesLoop(CancellationToken ct)
    {
        var currentSession = Process.GetCurrentProcess().SessionId;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var policy = _policy;
                if (_monitoringAllowed)
                {
                    var fg = _source.GetForeground()?.ProcessName;
                    var normFg = string.IsNullOrEmpty(fg) ? null : NormalizeProc(fg);
                    var runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (normFg is not null) runningNames.Add(normFg);

                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            if (p.SessionId != currentSession) continue;
                            runningNames.Add(NormalizeProc(p.ProcessName));
                        }
                        catch { /* Ignore inaccessible process */ }
                        finally { p.Dispose(); }
                    }

                    foreach (var shell in BlockedShells)
                    {
                        if (!runningNames.Contains(shell)) continue;
                        _appLockHost.TerminateApp(shell);
                    }

                    if (policy is not null)
                    {
                        foreach (var rule in policy.Rules)
                        {
                            var target = NormalizeProc(rule.ProcessName);
                            if (string.IsNullOrEmpty(target)) continue;

                            if (rule.Restriction == ApplicationRestrictionDto.Block && runningNames.Contains(target))
                            {
                                _appLockHost.TerminateApp(rule.ProcessName);
                                lock (_gate)
                                {
                                    if (_violationsReported.Add($"block:{target}"))
                                        _tracker.AddViolation(rule.ProcessName, DateTime.UtcNow, "blocked");
                                }
                                continue;
                            }

                            if (rule.RequirePassword && !string.IsNullOrEmpty(rule.PasswordHash))
                                EnforcePasswordLock(rule, runningNames, normFg);
                        }

                        if (policy.ProtectionEnabled && !string.IsNullOrEmpty(policy.ProtectionHash))
                        {
                            foreach (var name in ProtectedProcessNames)
                            {
                                var protRule = new AgentApplicationRuleDto(
                                    Guid.Empty, "Windows Sozlamalari — himoyalangan", $"{name}.exe", null, null,
                                    ApplicationClassificationDto.Neutral, ApplicationRestrictionDto.None, null,
                                    true, policy.ProtectionSalt, policy.ProtectionHash);
                                EnforcePasswordLock(protRule, runningNames, normFg);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "App rule enforcement iteration failed");
            }

            await Task.Delay(350, ct);
        }
    }

    // After unlock: ignore a couple of empty polls (UWP Settings can flicker) then re-lock once
    // the window is gone — never kill the process. Closing with X then reopening asks again.
    private const int UnlockGraceSeconds = 3;
    private const int UnlockHiddenPollsBeforeRelock = 3;

    private void EnforcePasswordLock(AgentApplicationRuleDto rule, HashSet<string> runningNames, string? normFg)
    {
        var target = NormalizeProc(rule.ProcessName);
        if (string.IsNullOrEmpty(target)) return;

        var isForeground = normFg is not null && string.Equals(normFg, target, StringComparison.OrdinalIgnoreCase);
        var isRunning = runningNames.Contains(target) || isForeground;
        var hasWindow = isForeground || _appLockHost.HasActiveVisibleWindow(rule.ProcessName);

        bool isUnlocked;
        DateTime unlockedAt;
        DateTime cooldownUntil;
        bool cooling;
        lock (_gate)
        {
            isUnlocked = _unlockedApps.TryGetValue(target, out unlockedAt);
            cooling = _lockCooldownUntil.TryGetValue(target, out cooldownUntil) && DateTime.UtcNow < cooldownUntil;
        }
        if (cooling) return;

        if (!isRunning && !hasWindow)
        {
            if (isUnlocked) CountHiddenPollAndMaybeRelock(target);
            else ClearUnlock(target);
            return;
        }

        if (isUnlocked)
        {
            if ((DateTime.UtcNow - unlockedAt).TotalSeconds < UnlockGraceSeconds || hasWindow)
            {
                ResetHiddenPolls(target);
                return;
            }
            CountHiddenPollAndMaybeRelock(target);
            return;
        }

        // Only challenge when the window is actually visible on screen (avoids prompting for suspended/background apps).
        if (!hasWindow) return;

        if (_appLockHost.ChallengeAndEnforce(rule))
        {
            lock (_gate)
            {
                _unlockedApps[target] = DateTime.UtcNow;
                _unlockHiddenPolls[target] = 0;
            }
        }
        else
        {
            ClearUnlock(target);
            lock (_gate)
            {
                _lockCooldownUntil[target] = DateTime.UtcNow.AddSeconds(2);
                _tracker.AddViolation(rule.ProcessName, DateTime.UtcNow, "password_denied");
            }
        }
    }

    private void ResetHiddenPolls(string target)
    {
        lock (_gate) _unlockHiddenPolls[target] = 0;
    }

    private void CountHiddenPollAndMaybeRelock(string target)
    {
        lock (_gate)
        {
            _unlockHiddenPolls.TryGetValue(target, out var n);
            n++;
            _unlockHiddenPolls[target] = n;
            if (n < UnlockHiddenPollsBeforeRelock) return;
            _unlockedApps.Remove(target);
            _unlockHiddenPolls.Remove(target);
        }
    }

    private void ClearUnlock(string target)
    {
        lock (_gate)
        {
            _unlockedApps.Remove(target);
            _unlockHiddenPolls.Remove(target);
        }
    }

    private static string NormalizeProc(string name) =>
        System.IO.Path.GetFileNameWithoutExtension(name.Trim());

    private void CheckRules(string process, DateTime now)
    {
        var day = DateOnly.FromDateTime(now.ToLocalTime());
        if (day != _today)
        {
            _today = day;
            _todayForeground.Clear();
            _violationsReported.Clear();
        }
        var interval = _policy?.SamplingIntervalSeconds ?? 3;
        _todayForeground[process] = _todayForeground.GetValueOrDefault(process) + interval;

        var target = NormalizeProc(process);
        var rule = _policy?.Rules.FirstOrDefault(r => string.Equals(NormalizeProc(r.ProcessName), target, StringComparison.OrdinalIgnoreCase));
        if (rule is null) return;
        if (rule.Restriction == ApplicationRestrictionDto.Block && _violationsReported.Add($"block:{target}"))
            _tracker.AddViolation(process, now, "blocked");
        if (rule.DailyLimitMinutes is { } limit && _todayForeground[process] > limit * 60 && _violationsReported.Add($"limit:{target}"))
            _tracker.AddViolation(process, now, "daily_limit");
    }

    private async Task SyncLoop(CancellationToken ct)
    {
        var lastPolicy = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - lastPolicy >= PolicyEvery || _policy is null)
                {
                    await RefreshPolicyAsync(ct);
                    lastPolicy = DateTime.UtcNow;
                }

                while (_monitoringAllowed && _queue.Peek() is { } item)
                {
                    var b = item.Batch;
                    var res = await _api!.SendBatchAsync(new TelemetryBatchRequest
                    {
                        BatchId = b.BatchId, AgentVersion = AgentInfo.Version, ApplicationEvents = b.ApplicationEvents, ActivitySessions = b.ActivitySessions,
                    }, ct);
                    if (res.Rejected > 0) _logger.LogWarning("Server rejected {Count} telemetry items", res.Rejected);
                    _queue.Remove(item.Path);
                    SetStatus(s => s with { LastSync = DateTime.UtcNow, State = AgentState.Online });
                }
            }
            catch (DeviceRejectedException ex) when (ex.Reason == ServerRejection.Blocked)
            {
                _monitoringAllowed = false;
                SetStatus(s => s with { State = AgentState.Blocked, MonitoringActive = false });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogDebug(ex, "Upload failed; data stays in the offline queue");
                SetStatus(s => s with { State = AgentState.Offline });
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                SetStatus(s => s with { State = AgentState.Offline });
            }
            SetStatus(s => s with { QueueLength = _queue.Count, RemoteChannelConnected = _remote?.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected });
            await PollChatAsync(ct);
            if (DateTime.UtcNow - _lastUpdateCheck >= TimeSpan.FromSeconds(45))
            {
                _lastUpdateCheck = DateTime.UtcNow;
                await CheckForUpdateAsync(ct);
            }
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
        }
    }

    public async Task CheckForUpdateNowAsync() => await CheckForUpdateAsync(CancellationToken.None);

    private async Task CheckForUpdateAsync(CancellationToken ct)
    {
        if (_config is null) return;
        try
        {
            var baseUri = new Uri(_config.ServerUrl.TrimEnd('/') + "/");
            using var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };
            var rel = await GetReleaseJsonAsync(http, "api/agent/release/latest.json", ct)
                      ?? await GetReleaseJsonAsync(http, "downloads/latest.json", ct);
            if (rel is null || string.IsNullOrWhiteSpace(rel.Version)) return;
            if (!Version.TryParse(rel.Version, out var latest) || !Version.TryParse(AgentInfo.Version, out var current)) return;
            if (latest <= current) return;

            AvailableUpdate = rel;
            if (_notifiedUpdateVersion != rel.Version)
            {
                _notifiedUpdateVersion = rel.Version;
                UpdateAvailable?.Invoke(rel);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Update check failed");
        }
    }

    public async Task<(bool Success, string Message)> InstallUpdateAsync(AvailableUpdateInfo release, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (_config is null) return (false, "Server manzili sozlanmagan.");
        try
        {
            await GatePipe.SendAsync(new GateRequest { Op = GatePipe.OpPrepareUpgrade }, TimeSpan.FromSeconds(8), ct);
            AgentSelfUpdate.ReleaseStale(release.Version);

            var baseUri = new Uri(_config.ServerUrl.TrimEnd('/') + "/");
            using var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(10) };
            var credential = AgentStore.LoadCredential();
            if (credential is not null)
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Device", credential);
            string? temp = null;
            try
            {
                temp = await AgentSelfUpdate.DownloadToTempAsync(http, release.Version, release.FileName, release.Size, progress, ct);

                if (!await AgentSelfUpdate.ShaMatchesAsync(temp, release.Sha256, ct))
                {
                    try { File.Delete(temp); } catch (IOException) { }
                    return (false, "Yuklangan fayl xesh summasi (SHA-256) mos kelmadi.");
                }

                progress?.Report(100);
                await GatePipe.SendAsync(new GateRequest { Op = GatePipe.OpPrepareUpgrade }, TimeSpan.FromSeconds(8), ct);
                var staged = AgentSelfUpdate.Stage(temp);
                var started = await GatePipe.SendAsync(new GateRequest { Op = GatePipe.OpRunSetup, Path = staged }, TimeSpan.FromSeconds(12), ct);
                if (started is not { Ok: true })
                    AgentSelfUpdate.Launch(staged);
                return (true, "Yangilanish o‘rnatilmoqda. Dastur o‘zi qayta ishga tushadi.");
            }
            catch (IOException)
            {
                AgentSelfUpdate.ReleaseStale(release.Version);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manual update install failed");
            return (false, $"Xatolik: {ex.Message}");
        }
    }

    private static async Task<AvailableUpdateInfo?> GetReleaseJsonAsync(HttpClient http, string path, CancellationToken ct)
    {
        using var resp = await http.GetAsync(path, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var json = (await resp.Content.ReadAsStringAsync(ct)).TrimStart('\uFEFF');
        return JsonSerializer.Deserialize<AvailableUpdateInfo>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private async Task PollChatAsync(CancellationToken ct)
    {
        if (_api is null) return;
        try
        {
            var poll = await _api.PollSupportAsync(_chatSince, ct);
            var newlyRead = new List<Guid>();
            foreach (var m in poll.Messages)
            {
                var fromAdmin = string.Equals(m.Sender, "Admin", StringComparison.OrdinalIgnoreCase);
                EmitChat(new ChatMessage(m.Id, fromAdmin, m.SenderName, m.Text, m.CreatedAt, m.ReadAt != null));
                if (!fromAdmin && m.ReadAt != null) newlyRead.Add(m.Id);
                if (m.CreatedAt > (_chatSince ?? DateTime.MinValue)) _chatSince = m.CreatedAt;
            }
            if (newlyRead.Count > 0) ChatRead?.Invoke(newlyRead);
        }
        catch (Exception ex) when (ex is not DeviceRejectedException)
        {
            _logger.LogDebug(ex, "Chat poll failed");
        }
    }

    /// <summary>Server "refresh-policy" push. A deleted device is rejected here, so the session ends at once.</summary>
    public async Task RefreshPolicyNowAsync()
    {
        if (_api is null) return;
        try
        {
            await RefreshPolicyAsync(CancellationToken.None);
        }
        catch (DeviceRejectedException ex) when (ex.Reason is ServerRejection.Revoked or ServerRejection.Invalid)
        {
            _rejection?.TrySetException(ex);
        }
    }

    private async Task RefreshPolicyAsync(CancellationToken ct)
    {
        var p = await _api!.GetPolicyAsync(ct);
        _policy = p;
        lock (_gate) _tracker.Settings = new TrackerSettings(p.IdleThresholdSeconds, p.WindowTitleCapture);
        SetStatus(s => s with { PolicyName = p.Name, PolicyVersion = p.Version, MonitoringNotice = p.MonitoringNotice, SupportContact = p.SupportContact, AutoUpdateEnabled = p.AutoUpdateEnabled });
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                CurrentActivity? current;
                lock (_gate) current = _tracker.Current;
                var res = await _api!.HeartbeatAsync(new HeartbeatRequest
                {
                    AgentVersion = AgentInfo.Version,
                    OsVersion = Environment.OSVersion.VersionString,
                    ClientTime = DateTime.UtcNow,
                    MonitoringEnabled = _monitoringAllowed,
                    QueueLength = _queue.Count,
                    PolicyId = _policy?.PolicyId,
                    PolicyVersion = _policy?.Version,
                    CurrentProcessName = current?.ProcessName,
                    CurrentWindowTitle = current?.WindowTitle,
                    CurrentState = current?.State,
                    CurrentActivityStartedAt = current?.Since,
                }, ct);
                _monitoringAllowed = res.MonitoringAllowed;
                if (res.PolicyVersion != _policy?.Version) await RefreshPolicyAsync(ct);
                if (_config is not null && res.LicenseExpiresAt != _config.LicenseExpiresAt)
                {
                    _config = _config with { LicenseExpiresAt = res.LicenseExpiresAt };
                    try { AgentStore.SaveConfig(_config); } catch (Exception) { /* Users may only have RX on ProgramData */ }
                }
                SetStatus(s => s with
                {
                    State = res.MonitoringAllowed ? AgentState.Online : AgentState.Blocked,
                    MonitoringActive = res.MonitoringAllowed,
                    LastSync = DateTime.UtcNow,
                    LicenseExpiresAt = res.LicenseExpiresAt ?? _config?.LicenseExpiresAt,
                });
            }
            catch (HttpRequestException)
            {
                SetStatus(s => s with { State = AgentState.Offline });
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                SetStatus(s => s with { State = AgentState.Offline });
            }
            await Task.Delay(TimeSpan.FromSeconds(_policy?.HeartbeatIntervalSeconds ?? 30), ct);
        }
    }

    private async Task LocationLoop(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), ct);
        while (!ct.IsCancellationRequested)
        {
            if (_policy is { LocationEnabled: true }) await ReportLocationOnceAsync(ct);
            else if (_policy is not null) SetStatus(s => s with { LocationInfo = "O‘chirilgan (administrator)" });
            var wait = Math.Clamp(_policy?.LocationIntervalSeconds ?? 300, 60, 3600);
            await Task.Delay(TimeSpan.FromSeconds(wait), ct);
        }
    }

    /// <summary>Administrator "locate now" request.</summary>
    public Task LocateNowAsync() =>
        _api is null || _policy is not { LocationEnabled: true } ? Task.CompletedTask : ReportLocationOnceAsync(CancellationToken.None);

    private async Task ReportLocationOnceAsync(CancellationToken ct)
    {
        if (_api is null || !await _locateGate.WaitAsync(0, ct)) return;
        try
        {
            var precise = _location is null ? null : await _location.GetAsync(ct);
            if (_ipLocation is null || DateTime.UtcNow - _ipLocationAt > TimeSpan.FromMinutes(30))
            {
                _ipLocation = await IpLocationLookup.LookupAsync(ct) ?? _ipLocation;
                _ipLocationAt = DateTime.UtcNow;
            }
            var ip = _ipLocation;

            DeviceLocationReportRequest? report = precise is not null
                ? new DeviceLocationReportRequest
                {
                    Latitude = precise.Latitude, Longitude = precise.Longitude, AccuracyMeters = precise.AccuracyMeters, Source = precise.Source,
                    City = ip?.City, Region = ip?.Region, Country = ip?.Country, Isp = ip?.Isp, CapturedAt = DateTime.UtcNow,
                }
                : ip is not null
                    ? new DeviceLocationReportRequest
                    {
                        Latitude = ip.Latitude, Longitude = ip.Longitude, AccuracyMeters = IpLocationLookup.ApproximateAccuracyMeters, Source = "ip",
                        City = ip.City, Region = ip.Region, Country = ip.Country, Isp = ip.Isp, CapturedAt = DateTime.UtcNow,
                    }
                    : null;

            if (report is null)
            {
                SetStatus(s => s with { LocationInfo = "Aniqlab bo‘lmadi" });
                return;
            }
            await _api.ReportLocationAsync(report, ct);
            var info = precise is not null
                ? $"Aniq — Windows joylashuv{(precise.AccuracyMeters is { } a ? $" (±{a:0} m)" : "")}"
                : $"Taxminiy — IP bo‘yicha{(ip?.City is { } c ? $" ({c})" : "")}";
            SetStatus(s => s with { LocationInfo = info });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Location report failed");
        }
        finally
        {
            _locateGate.Release();
        }
    }

    private void SetStatus(Func<AgentStatus, AgentStatus> update)
    {
        Status = update(Status);
        StatusChanged?.Invoke(Status);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            _tracker.Flush(now);
            var (events, sessions) = _tracker.Drain(now, TimeSpan.Zero);
            _queue.Enqueue(events, sessions, now);
        }
        if (_remote is not null) await _remote.DisposeAsync();
        _api?.Dispose();
    }
}
