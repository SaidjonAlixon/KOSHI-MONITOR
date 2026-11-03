using System.Runtime.Versioning;
using KOSHI.Agent.Core.Remote;
using KOSHI.Agent.Core.Windows;
using KOSHI.Contracts;
using Microsoft.Extensions.Logging;

namespace KOSHI.Agent.Core;

public enum AgentState { NotActivated, Connecting, Online, Offline, Blocked, Revoked }

/// <summary>Implemented by the tray app: shows the password dialog for locked apps and terminates refused apps.</summary>
public interface IAppLockHost
{
    /// <summary>Called when a password-protected app is in the foreground and not yet unlocked.
    /// Returns true to allow it, false to close it. Verify with <see cref="AppLock.Verify"/>.</summary>
    bool ChallengeAndEnforce(AgentApplicationRuleDto rule);
}

public static class AppLock
{
    public static bool Verify(AgentApplicationRuleDto rule, string typedPassword) =>
        KOSHI.Contracts.AppLockPassword.Verify(rule.PasswordSalt, rule.PasswordHash, typedPassword);
}

public sealed record ChatMessage(Guid Id, bool FromAdmin, string? SenderName, string Text, DateTime CreatedAt);

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
    bool AutoUpdateEnabled);

/// <summary>Runs inside the interactive user session: sampling, local queue, upload, heartbeat, policy and remote channel.</summary>
[SupportedOSPlatform("windows")]
public sealed class AgentRuntime : IAsyncDisposable
{
    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SplitOpenAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PolicyEvery = TimeSpan.FromMinutes(5);

    private readonly IActivitySource _source;
    private readonly IRemoteHost _remoteHost;
    private readonly IAppLockHost _appLockHost;
    private readonly ILogger _logger;
    private readonly HashSet<string> _unlockedApps = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _chatSince;
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
    private bool _monitoringAllowed = true;
    private DateOnly _today;

    public AgentRuntime(IActivitySource source, IRemoteHost remoteHost, IAppLockHost appLockHost, ILogger logger)
    {
        _source = source;
        _remoteHost = remoteHost;
        _appLockHost = appLockHost;
        _logger = logger;
        Status = new AgentStatus(AgentState.NotActivated, null, null, false, null, null, 0, null, DefaultNotice, "IT bo'limi", false, true);
    }

    public const string DefaultNotice = "Korporativ kompyuter monitoringi faol.";

    public AgentStatus Status { get; private set; }

    public event Action<AgentStatus>? StatusChanged;

    /// <summary>Raised for each support chat message (from admin, and echoes of the agent's own sent messages).</summary>
    public event Action<ChatMessage>? ChatReceived;

    public async Task<bool> SendChatAsync(string text)
    {
        if (_api is null || string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var m = await _api.SendSupportAsync(text.Trim(), CancellationToken.None);
            ChatReceived?.Invoke(new ChatMessage(m.Id, false, null, m.Text, m.CreatedAt));
            if (m.CreatedAt > (_chatSince ?? DateTime.MinValue)) _chatSince = m.CreatedAt;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Chat send failed");
            return false;
        }
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
            if (_config is null || credential is null)
            {
                SetStatus(s => s with { State = AgentState.NotActivated, MonitoringActive = false });
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                continue;
            }

            SetStatus(s => s with { State = AgentState.Connecting, ServerUrl = _config.ServerUrl, DeviceName = _config.DisplayName });
            _api = new KoshiApiClient(_config.ServerUrl, credential);
            _remote = new RemoteConnection(_api.BaseAddress, credential, _remoteHost, _logger);
            _ = _remote.StartAsync(ct);

            try
            {
                await Task.WhenAll(SampleLoop(ct), SyncLoop(ct), HeartbeatLoop(ct));
            }
            catch (DeviceRejectedException ex)
            {
                _logger.LogWarning("Server rejected device: {Reason}", ex.Reason);
                if (ex.Reason is ServerRejection.Revoked or ServerRejection.Invalid)
                {
                    SetStatus(s => s with { State = AgentState.Revoked, MonitoringActive = false });
                    await _remote.DisposeAsync();
                    _remote = null;
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
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

            EnforceAppLock();
            await Task.Delay(TimeSpan.FromSeconds(_policy?.SamplingIntervalSeconds ?? 3), ct);
        }
    }

    /// <summary>Reports restricted applications and exceeded daily limits as policy violations (audit). Enforcement is done by the service via AppLocker.</summary>
    private void CheckRules(string process, DateTime now)
    {
        var day = DateOnly.FromDateTime(now.ToLocalTime());
        if (day != _today)
        {
            _today = day;
            _todayForeground.Clear();
            _violationsReported.Clear();
            _unlockedApps.Clear();
        }
        var interval = _policy?.SamplingIntervalSeconds ?? 3;
        _todayForeground[process] = _todayForeground.GetValueOrDefault(process) + interval;

        var rule = _policy?.Rules.FirstOrDefault(r => string.Equals(r.ProcessName, process, StringComparison.OrdinalIgnoreCase));
        if (rule is null) return;
        if (rule.Restriction == ApplicationRestrictionDto.Block && _violationsReported.Add($"block:{process}"))
            _tracker.AddViolation(process, now, "blocked");
        if (rule.DailyLimitMinutes is { } limit && _todayForeground[process] > limit * 60 && _violationsReported.Add($"limit:{process}"))
            _tracker.AddViolation(process, now, "daily_limit");
        if (rule.RequirePassword && !string.IsNullOrEmpty(rule.PasswordHash) && !_unlockedApps.Contains(process))
            _pendingLock = rule;
    }

    private AgentApplicationRuleDto? _pendingLock;
    private volatile bool _challengeInProgress;

    /// <summary>Shows the password prompt for a locked app outside the sampling lock. Correct password unlocks for the session.</summary>
    private void EnforceAppLock()
    {
        AgentApplicationRuleDto? rule;
        lock (_gate)
        {
            rule = _pendingLock;
            _pendingLock = null;
        }
        if (rule is null || _challengeInProgress) return;
        _challengeInProgress = true;
        try
        {
            if (_appLockHost.ChallengeAndEnforce(rule))
                lock (_gate) _unlockedApps.Add(rule.ProcessName);
            else
                _tracker.AddViolation(rule.ProcessName, DateTime.UtcNow, "password_denied");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "App-lock enforcement failed");
        }
        finally
        {
            _challengeInProgress = false;
        }
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
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
        }
    }

    private async Task PollChatAsync(CancellationToken ct)
    {
        if (_api is null) return;
        try
        {
            var poll = await _api.PollSupportAsync(_chatSince, ct);
            foreach (var m in poll.Messages)
            {
                var fromAdmin = string.Equals(m.Sender, "Admin", StringComparison.OrdinalIgnoreCase);
                if (fromAdmin) ChatReceived?.Invoke(new ChatMessage(m.Id, true, m.SenderName, m.Text, m.CreatedAt));
                if (m.CreatedAt > (_chatSince ?? DateTime.MinValue)) _chatSince = m.CreatedAt;
            }
        }
        catch (Exception ex) when (ex is not DeviceRejectedException)
        {
            _logger.LogDebug(ex, "Chat poll failed");
        }
    }

    public Task RefreshPolicyNowAsync() => _api is null ? Task.CompletedTask : RefreshPolicyAsync(CancellationToken.None);

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
                SetStatus(s => s with
                {
                    State = res.MonitoringAllowed ? AgentState.Online : AgentState.Blocked,
                    MonitoringActive = res.MonitoringAllowed,
                    LastSync = DateTime.UtcNow,
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
