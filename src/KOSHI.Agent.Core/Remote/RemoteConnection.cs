using KOSHI.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace KOSHI.Agent.Core.Remote;

/// <summary>Implemented by the tray app: screen capture, input injection and command execution.</summary>
public interface IRemoteHost
{
    /// <summary>Session start hook. Live view stays silent on the employee PC (no banner, no close button).</summary>
    void ShowSessionBanner(Guid sessionId, string adminName, Action endByEmployee);

    void HideSessionBanner(Guid sessionId);

    /// <summary>Captures the primary screen as JPEG; returns null when capture is not possible (e.g. locked desktop).</summary>
    (byte[] Jpeg, int Width, int Height)? CaptureFrame(int maxWidth, int quality);

    void InjectInput(RemoteInputDto input);

    Task<(bool Success, string Message)> ExecuteAsync(RemoteCommandDto command);
}

/// <summary>Agent end of the /hubs/agent channel. Streams frames only while an admin session is active.</summary>
public sealed class RemoteConnection : IAsyncDisposable
{
    private readonly HubConnection _hub;
    private readonly IRemoteHost _host;
    private readonly ILogger _logger;
    private CancellationTokenSource? _streamCts;
    private Guid? _sessionId;

    public event Action<SupportMessageDto>? ChatMessageReceived;
    public event Action<SupportMessageDto>? ChatMessageUpdated;
    public event Action<Guid>? ChatMessageDeleted;
    public event Action<Guid[]>? ChatMessagesRead;
    public event Action? ChatCleared;
    public event Action<string>? ChatTypingReceived;

    public RemoteConnection(Uri server, string credential, IRemoteHost host, ILogger logger)
    {
        _host = host;
        _logger = logger;
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server, "hubs/agent"), o => o.Headers["Authorization"] = $"Device {credential}")
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        _hub.On<RemoteSessionStartDto>("remoteStart", StartSession);
        _hub.On<Guid>("remoteStop", id => StopSession(id, notifyServer: false, reason: "admin"));
        _hub.On<Guid, RemoteInputDto>("remoteInput", (id, input) =>
        {
            if (_sessionId == id) _host.InjectInput(input);
        });
        _hub.On<RemoteCommandDto>("command", async cmd =>
        {
            var (ok, message) = RemoteCommands.All.Contains(cmd.Command)
                ? await _host.ExecuteAsync(cmd)
                : (false, "Unsupported command");
            await SafeInvoke("CommandResult", cmd.CommandId, ok, message);
        });
        _hub.On<SupportMessageDto>("chatMessage", msg => ChatMessageReceived?.Invoke(msg));
        _hub.On<SupportMessageDto>("chatMessageUpdated", msg => ChatMessageUpdated?.Invoke(msg));
        _hub.On<Guid>("chatMessageDeleted", id => ChatMessageDeleted?.Invoke(id));
        _hub.On<Guid[]>("chatMessagesRead", ids => ChatMessagesRead?.Invoke(ids));
        _hub.On("chatCleared", () => ChatCleared?.Invoke());
        _hub.On<string>("chatTyping", name => ChatTypingReceived?.Invoke(name));
        _hub.Closed += _ =>
        {
            if (_sessionId is { } id) StopSession(id, notifyServer: false, reason: "connection_closed");
            return Task.CompletedTask;
        };
    }

    public HubConnectionState State => _hub.State;

    public Task SendTypingAsync() => SafeInvoke("ChatTyping");
    public Task SendMarkReadAsync() => SafeInvoke("ChatMarkRead");

    public async Task StartAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _hub.State == HubConnectionState.Disconnected)
        {
            try
            {
                await _hub.StartAsync(ct);
                _logger.LogInformation("Remote channel connected");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Remote channel connect failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
        }
    }

    private void StartSession(RemoteSessionStartDto s)
    {
        if (_sessionId is { } existing) StopSession(existing, notifyServer: true, reason: "replaced");
        _sessionId = s.SessionId;
        // Live view is silent: the employee must not see a banner or be able to end the session.
        _host.ShowSessionBanner(s.SessionId, s.AdminName, endByEmployee: () => { });
        _streamCts = new CancellationTokenSource();
        _ = StreamAsync(s, _streamCts.Token);
        _logger.LogInformation("Remote session {SessionId} started", s.SessionId);
    }

    private async Task StreamAsync(RemoteSessionStartDto s, CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, s.Fps));
        while (!ct.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            try
            {
                if (_host.CaptureFrame(s.MaxWidth, s.Quality) is { } frame)
                    await _hub.InvokeAsync("Frame", s.SessionId, frame.Jpeg, frame.Width, frame.Height, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Frame send failed");
                await Task.Delay(500, CancellationToken.None);
            }
            var wait = interval - (DateTime.UtcNow - started);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, CancellationToken.None);
        }
    }

    private void StopSession(Guid id, bool notifyServer, string reason)
    {
        if (_sessionId != id) return;
        _sessionId = null;
        _streamCts?.Cancel();
        _streamCts = null;
        _host.HideSessionBanner(id);
        if (notifyServer) _ = SafeInvoke("SessionEnded", id, reason);
        _logger.LogInformation("Remote session {SessionId} ended ({Reason})", id, reason);
    }

    private async Task SafeInvoke(string method, params object?[] args)
    {
        try
        {
            if (_hub.State == HubConnectionState.Connected) await _hub.SendCoreAsync(method, args);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Hub call {Method} failed", method);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_sessionId is { } id) StopSession(id, notifyServer: true, reason: "agent_stopped");
        await _hub.DisposeAsync();
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext ctx) => TimeSpan.FromSeconds(Math.Min(30, 2 + ctx.PreviousRetryCount * 3));
    }
}
