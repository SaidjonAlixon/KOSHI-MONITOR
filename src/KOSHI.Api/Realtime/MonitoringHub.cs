using KOSHI.Api.Security;
using KOSHI.Application.Abstractions;
using KOSHI.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KOSHI.Api.Realtime;

/// <summary>
/// Real-time channel for the Live Monitoring page. Only an invalidation signal (device id) is pushed;
/// clients re-fetch data through the normal, permission-checked REST endpoints.
/// </summary>
[Authorize(Policy = Permissions.MonitoringLive, AuthenticationSchemes = AuthSchemes.Admin)]
public sealed class MonitoringHub : Hub
{
    public const string Path = "/hubs/monitoring";
}

public sealed class SignalRMonitoringNotifier(IHubContext<MonitoringHub> hub, ILogger<SignalRMonitoringNotifier> logger) : IMonitoringNotifier
{
    public async Task DeviceChangedAsync(Guid deviceId, CancellationToken ct = default)
    {
        try
        {
            await hub.Clients.All.SendAsync("deviceChanged", deviceId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime notification failed");
        }
    }
}
