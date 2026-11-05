using System.Collections.Concurrent;
using System.Security.Claims;
using KOSHI.Api.Security;
using KOSHI.Application.Abstractions;
using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using KOSHI.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Api.Realtime;

public sealed record RemoteSession(Guid SessionId, Guid DeviceId, string AdminConnectionId, Guid AdminUserId, string AdminEmail, DateTime StartedAt);

/// <summary>Tracks connected agents and active remote sessions (single API instance; use a backplane to scale out).</summary>
public sealed class RemoteRegistry
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _agents = new();
    private readonly ConcurrentDictionary<Guid, RemoteSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, string> _commands = new();

    public void AgentConnected(Guid deviceId, string connectionId) => _agents.GetOrAdd(deviceId, _ => new())[connectionId] = 0;

    public void AgentDisconnected(Guid deviceId, string connectionId)
    {
        if (_agents.TryGetValue(deviceId, out var set)) set.TryRemove(connectionId, out _);
    }

    public bool IsAgentConnected(Guid deviceId) => _agents.TryGetValue(deviceId, out var set) && !set.IsEmpty;

    public IReadOnlyCollection<Guid> ConnectedDevices => _agents.Where(a => !a.Value.IsEmpty).Select(a => a.Key).ToList();

    public void Add(RemoteSession s) => _sessions[s.SessionId] = s;

    public RemoteSession? Get(Guid id) => _sessions.TryGetValue(id, out var s) ? s : null;

    public RemoteSession? Remove(Guid id) => _sessions.TryRemove(id, out var s) ? s : null;

    public IReadOnlyList<RemoteSession> OwnedBy(string connectionId) => _sessions.Values.Where(s => s.AdminConnectionId == connectionId).ToList();

    public IReadOnlyList<RemoteSession> ForDevice(Guid deviceId) => _sessions.Values.Where(s => s.DeviceId == deviceId).ToList();

    public void TrackCommand(Guid commandId, string adminConnectionId) => _commands[commandId] = adminConnectionId;

    public string? TakeCommand(Guid commandId) => _commands.TryRemove(commandId, out var c) ? c : null;
}

/// <summary>Agent side of remote control. Authenticated with the device credential.</summary>
[Authorize(Policy = AuthSchemes.DevicePolicy)]
public sealed class AgentHub(RemoteRegistry registry, IHubContext<RemoteHub> admins, IHubContext<MonitoringHub> monitoring, IServiceScopeFactory scopes, ILogger<AgentHub> logger) : Hub
{
    public const string Path = "/hubs/agent";

    private Guid DeviceId => Context.User!.DeviceId();

    public override async Task OnConnectedAsync()
    {
        if (!Context.User!.IsDeviceStatus(DeviceStatus.Active))
        {
            Context.Abort();
            return;
        }
        registry.AgentConnected(DeviceId, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(DeviceId));
        using (var scope = scopes.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DeviceService>().MarkConnectedAsync(DeviceId, Context.ConnectionId);
        await monitoring.Clients.All.SendAsync("deviceChanged", DeviceId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var deviceId = DeviceId;
        registry.AgentDisconnected(deviceId, Context.ConnectionId);
        using (var scope = scopes.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DeviceService>().MarkDisconnectedAsync(deviceId, Context.ConnectionId);
        await monitoring.Clients.All.SendAsync("deviceChanged", deviceId);
        if (!registry.IsAgentConnected(deviceId))
        {
            foreach (var s in registry.ForDevice(deviceId))
            {
                registry.Remove(s.SessionId);
                await admins.Clients.Client(s.AdminConnectionId).SendAsync("sessionEnded", s.SessionId, "Agent aloqasi uzildi");
                await AuditAsync(s, "remote.session_end", new { reason = "agent_disconnected" });
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>JPEG frame of the remote screen for an active session owned by this device.</summary>
    public async Task Frame(Guid sessionId, byte[] jpeg, int width, int height)
    {
        var s = registry.Get(sessionId);
        if (s is null || s.DeviceId != DeviceId) return;
        await admins.Clients.Client(s.AdminConnectionId).SendAsync("frame", sessionId, jpeg, width, height);
    }

    /// <summary>The agent ended the session (disconnect, replace, or process stop).</summary>
    public async Task SessionEnded(Guid sessionId, string reason)
    {
        var s = registry.Get(sessionId);
        if (s is null || s.DeviceId != DeviceId) return;
        registry.Remove(sessionId);
        await admins.Clients.Client(s.AdminConnectionId).SendAsync("sessionEnded", sessionId, reason);
        await AuditAsync(s, "remote.session_end", new { reason = Truncate(reason, 200), endedBy = "agent" });
    }

    public async Task CommandResult(Guid commandId, bool success, string message)
    {
        var admin = registry.TakeCommand(commandId);
        if (admin is not null) await admins.Clients.Client(admin).SendAsync("commandResult", commandId, success, Truncate(message, 500));
        using var scope = scopes.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditWriter>();
        await audit.RecordAndSaveAsync("remote.command_result", "Device", DeviceId, new { commandId, success, message = Truncate(message, 300) }, success, deviceId: DeviceId);
    }

    public async Task ChatTyping() =>
        await monitoring.Clients.All.SendAsync("supportTyping", DeviceId);

    public async Task ChatMarkRead()
    {
        using var scope = scopes.CreateScope();
        var support = scope.ServiceProvider.GetRequiredService<SupportService>();
        if (await support.MarkReadByDeviceAsync(DeviceId))
            await monitoring.Clients.All.SendAsync("supportMessagesRead", DeviceId);
    }

    public static string Group(Guid deviceId) => $"device:{deviceId:N}";

    private async Task AuditAsync(RemoteSession s, string action, object details)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IKoshiDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(), OccurredAt = DateTime.UtcNow, ActorType = AuditActorType.Device, ActorDeviceId = s.DeviceId,
                Action = action, EntityType = "RemoteSession", EntityId = s.SessionId.ToString(),
                Details = System.Text.Json.JsonSerializer.Serialize(details),
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to audit remote session event");
        }
    }

    private static string Truncate(string? s, int max) => s is null ? string.Empty : s.Length <= max ? s : s[..max];
}

/// <summary>Admin side of remote control. Requires the remote.control permission.</summary>
[Authorize(Policy = Permissions.RemoteControl, AuthenticationSchemes = AuthSchemes.Admin)]
public sealed class RemoteHub(RemoteRegistry registry, IHubContext<AgentHub> agents, IServiceScopeFactory scopes) : Hub
{
    public const string Path = "/hubs/remote";
    private static readonly string[] InputTypes = ["move", "down", "up", "wheel", "hwheel", "keydown", "keyup"];

    private Guid UserId => Context.User!.UserId();
    private string Email => Context.User!.FindFirstValue(ClaimTypes.Email) ?? Context.User!.FindFirstValue("email") ?? "admin";
    private string DisplayName => Context.User!.FindFirstValue(ClaimTypes.Name) ?? Email;

    public bool IsAgentConnected(Guid deviceId) => registry.IsAgentConnected(deviceId);

    public async Task<Guid> Start(Guid deviceId, int quality = 55, int maxWidth = 1600, int fps = 10)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IKoshiDbContext>();
        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deviceId && d.DeletedAt == null)
                     ?? throw new HubException("Qurilma topilmadi.");
        if (device.Status != DeviceStatus.Active) throw new HubException("Qurilma faol emas (bloklangan yoki bekor qilingan).");
        if (!registry.IsAgentConnected(deviceId)) throw new HubException("Agent hozir onlayn emas.");

        var session = new RemoteSession(Guid.NewGuid(), deviceId, Context.ConnectionId, UserId, Email, DateTime.UtcNow);
        registry.Add(session);
        await AuditAsync(scope, "remote.session_start", session.SessionId, deviceId, new { device.DisplayName });
        await agents.Clients.Group(AgentHub.Group(deviceId)).SendAsync("remoteStart",
            new RemoteSessionStartDto(session.SessionId, DisplayName, Math.Clamp(maxWidth, 640, 2560), Math.Clamp(quality, 20, 90), Math.Clamp(fps, 2, 20)));
        return session.SessionId;
    }

    public async Task Input(Guid sessionId, RemoteInputDto input)
    {
        var s = registry.Get(sessionId);
        if (s is null || s.AdminConnectionId != Context.ConnectionId) return;
        if (!InputTypes.Contains(input.Type) || input.X is < 0 or > 1 || input.Y is < 0 or > 1) return;
        await agents.Clients.Group(AgentHub.Group(s.DeviceId)).SendAsync("remoteInput", sessionId, input);
    }

    public async Task Stop(Guid sessionId)
    {
        var s = registry.Get(sessionId);
        if (s is null || s.AdminConnectionId != Context.ConnectionId) return;
        await EndAsync(s, "admin");
    }

    public async Task<Guid> Command(Guid deviceId, string command, string? argument)
    {
        if (!RemoteCommands.All.Contains(command)) throw new HubException("Noma'lum buyruq.");
        if (argument is { Length: > 500 }) throw new HubException("Argument juda uzun.");
        if (!registry.IsAgentConnected(deviceId)) throw new HubException("Agent hozir onlayn emas.");
        var commandId = Guid.NewGuid();
        registry.TrackCommand(commandId, Context.ConnectionId);
        using var scope = scopes.CreateScope();
        await AuditAsync(scope, "remote.command", commandId, deviceId, new { command, argument = command == RemoteCommands.Message ? "[message]" : argument });
        await agents.Clients.Group(AgentHub.Group(deviceId)).SendAsync("command", new RemoteCommandDto(commandId, command, argument, DisplayName));
        return commandId;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var s in registry.OwnedBy(Context.ConnectionId)) await EndAsync(s, "admin_disconnected");
        await base.OnDisconnectedAsync(exception);
    }

    private async Task EndAsync(RemoteSession s, string reason)
    {
        registry.Remove(s.SessionId);
        await agents.Clients.Group(AgentHub.Group(s.DeviceId)).SendAsync("remoteStop", s.SessionId);
        using var scope = scopes.CreateScope();
        await AuditAsync(scope, "remote.session_end", s.SessionId, s.DeviceId, new { reason, seconds = (int)(DateTime.UtcNow - s.StartedAt).TotalSeconds });
    }

    private async Task AuditAsync(IServiceScope scope, string action, Guid entityId, Guid deviceId, object details)
    {
        var db = scope.ServiceProvider.GetRequiredService<IKoshiDbContext>();
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), OccurredAt = DateTime.UtcNow, ActorType = AuditActorType.User, ActorUserId = UserId, ActorEmail = Email,
            Action = action, EntityType = "Device", EntityId = deviceId.ToString(),
            IpAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString(),
            Details = System.Text.Json.JsonSerializer.Serialize(new { entityId, details }),
        });
        await db.SaveChangesAsync();
    }
}
