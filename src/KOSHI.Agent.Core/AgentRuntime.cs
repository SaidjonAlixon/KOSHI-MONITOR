using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Security.Cryptography;
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

/// <summary>Runs inside the interactive user session: sampling, local queue, upload, heartbeat, policy, app enforcement and real-time chat/remote channel.</summary>
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
    private bool _monitoringAllowed = true;
    private DateOnly _today;

    public AgentRuntime(IActivitySource source, IRemoteHost remoteHost, IAppLockHost appLockHost, ILogger logger)
    {
        _source = source;
        _remoteHost = remoteHost;
        _appLockHost = appLockHost;
        _logger = logger;
        Status = new AgentStatus(AgentState.NotActivated, null, null, false, null, null, 0, null, DefaultNotice, "", false, true);
    }

    public const string DefaultNotice = "Korporativ kompyuter monitoringi faol.";

    public AgentStatus Status { get; private set; }

    public IReadOnlyList<ChatMessage> ChatHistory
    {
        get { lock (_gate) return [.. _chatHistory]; }
    }

    public event Action<AgentStatus>? StatusChanged;

    /// <summary>Raised for each support chat message (from admin, and echoes of the agent's own sent messages).</summary>
    public event Action<ChatMessage>? ChatReceived;

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
            if (_config is null || credential is null)
            {
                SetStatus(s => s with { State = AgentState.NotActivated, MonitoringActive = false });
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                continue;
            }

            SetStatus(s => s with { State = AgentState.Connecting, ServerUrl = _config.ServerUrl, DeviceName = _config.DisplayName });
            _api = new KoshiApiClient(_config.ServerUrl, credential);
            _remote = new RemoteConnection(_api.BaseAddress, credential, _remoteHost, _logger);
            _remote.ChatMessageReceived += m =>
            {
                var fromAdmin = string.Equals(m.Sender, "Admin", StringComparison.OrdinalIgnoreCase);
                EmitChat(new ChatMessage(m.Id, fromAdmin, m.SenderName, m.Text, m.CreatedAt));
                if (m.CreatedAt > (_chatSince ?? DateTime.MinValue)) _chatSince = m.CreatedAt;
            };
            _remote.ChatTypingReceived += name => AdminTyping?.Invoke(name);
            _ = _remote.StartAsync(ct);

            try
            {
                await Task.WhenAll(SampleLoop(ct), EnforceRulesLoop(ct), SyncLoop(ct), HeartbeatLoop(ct));
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

    /// <summary>Fast 750ms enforcement loop: closes blocked apps immediately and prompts for password-locked apps as soon as they open.</summary>
    private async Task EnforceRulesLoop(CancellationToken ct)
    {
        var currentSession = Process.GetCurrentProcess().SessionId;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var rules = _policy?.Rules;
                if (_monitoringAllowed && rules is { Count: > 0 })
                {
                    var fg = _source.GetForeground()?.ProcessName;
                    var runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var windowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrEmpty(fg))
                    {
                        var normFg = NormalizeProc(fg);
                        runningNames.Add(normFg);
                        windowedNames.Add(normFg);
                    }

                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            if (p.SessionId != currentSession) continue;
                            var norm = NormalizeProc(p.ProcessName);
                            runningNames.Add(norm);
                            if (p.MainWindowHandle != 0) windowedNames.Add(norm);
                        }
                        catch
                        {
                            // Ignore inaccessible process
                        }
                        finally
                        {
                            p.Dispose();
                        }
                    }

                    // If a previously unlocked app was closed by the user, require the password again next time it opens.
                    lock (_gate)
                    {
                        _unlockedApps.RemoveWhere(u => !runningNames.Contains(NormalizeProc(u)));
                    }

                    foreach (var rule in rules)
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

                        bool isUnlocked;
                        lock (_gate) isUnlocked = _unlockedApps.Contains(target);

                        if (rule.RequirePassword && !string.IsNullOrEmpty(rule.PasswordHash) && !isUnlocked && windowedNames.Contains(target))
                        {
                            if (_appLockHost.ChallengeAndEnforce(rule))
                            {
                                lock (_gate) _unlockedApps.Add(target);
                            }
                            else
                            {
                                lock (_gate) _tracker.AddViolation(rule.ProcessName, DateTime.UtcNow, "password_denied");
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "App rule enforcement iteration failed");
            }

            await Task.Delay(750, ct);
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
            var baseUri = new Uri(_config.ServerUrl.TrimEnd('/') + "/");
            using var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(10) };
            var temp = Path.Combine(Path.GetTempPath(), $"KOSHI-Agent-Setup-{release.Version}.exe");

            var resp = await http.GetAsync("api/agent/release/download", HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                resp.Dispose();
                var fileName = Path.GetFileName(release.FileName);
                resp = await http.GetAsync($"downloads/{Uri.EscapeDataString(fileName)}", HttpCompletionOption.ResponseHeadersRead, ct);
            }

            using (resp)
            {
                if (!resp.IsSuccessStatusCode) return (false, $"Yuklab bo‘lmadi (HTTP {(int)resp.StatusCode}).");
                var total = resp.Content.Headers.ContentLength ?? release.Size;
                await using var input = await resp.Content.ReadAsStreamAsync(ct);
                await using var fs = File.Create(temp);
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    readTotal += read;
                    if (total > 0) progress?.Report((int)Math.Clamp(readTotal * 100 / total, 1, 99));
                }
            }

            if (!string.IsNullOrWhiteSpace(release.Sha256))
            {
                await using var verifyStream = File.OpenRead(temp);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(verifyStream, ct));
                if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(temp);
                    return (false, "Yuklangan fayl xesh summasi (SHA-256) mos kelmadi.");
                }
            }

            progress?.Report(100);
            Process.Start(new ProcessStartInfo(temp, "/SILENT /NORESTART /SUPPRESSMSGBOXES") { UseShellExecute = true });
            return (true, "Yangilanish o‘rnatilmoqda…");
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
            foreach (var m in poll.Messages)
            {
                var fromAdmin = string.Equals(m.Sender, "Admin", StringComparison.OrdinalIgnoreCase);
                EmitChat(new ChatMessage(m.Id, fromAdmin, m.SenderName, m.Text, m.CreatedAt));
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
