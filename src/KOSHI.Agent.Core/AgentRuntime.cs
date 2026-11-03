using System.Runtime.Versioning;
using KOSHI.Agent.Core.Remote;
using KOSHI.Agent.Core.Windows;
using KOSHI.Contracts;
using Microsoft.Extensions.Logging;

namespace KOSHI.Agent.Core;

public enum AgentState { NotActivated, Connecting, Online, Offline, Blocked, Revoked }

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
    bool RemoteChannelConnected);

/// <summary>Runs inside the interactive user session: sampling, local queue, upload, heartbeat, policy and remote channel.</summary>
[SupportedOSPlatform("windows")]
public sealed class AgentRuntime : IAsyncDisposable
{
    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SplitOpenAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PolicyEvery = TimeSpan.FromMinutes(5);

    private readonly IActivitySource _source;
    private readonly IRemoteHost _remoteHost;
    private readonly ILogger _logger;
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

    public AgentRuntime(IActivitySource source, IRemoteHost remoteHost, ILogger logger)
    {
        _source = source;
        _remoteHost = remoteHost;
        _logger = logger;
        Status = new AgentStatus(AgentState.NotActivated, null, null, false, null, null, 0, null, DefaultNotice, "IT bo'limi", false);
    }

    public const string DefaultNotice = "Bu korporativ kompyuter tashkilot siyosatiga muvofiq KOSHI orqali monitoring qilinadi. Klaviatura bosishlari, parollar, clipboard, kamera va mikrofon yig'ilmaydi.";

    public AgentStatus Status { get; private set; }

    public event Action<AgentStatus>? StatusChanged;

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
        }
        var interval = _policy?.SamplingIntervalSeconds ?? 3;
        _todayForeground[process] = _todayForeground.GetValueOrDefault(process) + interval;

        var rule = _policy?.Rules.FirstOrDefault(r => string.Equals(r.ProcessName, process, StringComparison.OrdinalIgnoreCase));
        if (rule is null) return;
        if (rule.Restriction == ApplicationRestrictionDto.Block && _violationsReported.Add($"block:{process}"))
            _tracker.AddViolation(process, now, "blocked");
        if (rule.DailyLimitMinutes is { } limit && _todayForeground[process] > limit * 60 && _violationsReported.Add($"limit:{process}"))
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
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
        }
    }

    public Task RefreshPolicyNowAsync() => _api is null ? Task.CompletedTask : RefreshPolicyAsync(CancellationToken.None);

    private async Task RefreshPolicyAsync(CancellationToken ct)
    {
        var p = await _api!.GetPolicyAsync(ct);
        _policy = p;
        lock (_gate) _tracker.Settings = new TrackerSettings(p.IdleThresholdSeconds, p.WindowTitleCapture);
        SetStatus(s => s with { PolicyName = p.Name, PolicyVersion = p.Version, MonitoringNotice = p.MonitoringNotice, SupportContact = p.SupportContact });
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
