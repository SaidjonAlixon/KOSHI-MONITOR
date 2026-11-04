using KOSHI.Api.Realtime;
using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace KOSHI.Api.Controllers;

/// <summary>Device location map and history for administrators.</summary>
[ApiController]
[Route("api/locations")]
[Authorize(Policy = Permissions.LocationRead)]
public sealed class LocationController(LocationService locations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceLocationDto>>> Latest([FromQuery] Guid? departmentId, CancellationToken ct) =>
        Ok(await locations.LatestAsync(departmentId, ct));

    [HttpGet("{deviceId:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<LocationPointDto>>> History(Guid deviceId, [FromQuery] int days = 7, CancellationToken ct = default) =>
        Ok(await locations.HistoryAsync(deviceId, days, ct));

    /// <summary>Asks the online agent to determine and report its position right now.</summary>
    [HttpPost("{deviceId:guid}/locate")]
    public async Task<IActionResult> LocateNow(Guid deviceId, [FromServices] RemoteRegistry registry, [FromServices] IHubContext<AgentHub> agents,
        [FromServices] AuditWriter audit, CancellationToken ct)
    {
        if (!registry.IsAgentConnected(deviceId)) return Conflict(new { code = ErrorCodes.Conflict, title = "Agent hozir onlayn emas." });
        await agents.Clients.Group(AgentHub.Group(deviceId)).SendAsync("command",
            new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.LocateNow, null, User.Identity?.Name ?? "Admin"), ct);
        await audit.RecordAndSaveAsync("location.locate_now", "Device", deviceId, ct: ct);
        return NoContent();
    }
}
