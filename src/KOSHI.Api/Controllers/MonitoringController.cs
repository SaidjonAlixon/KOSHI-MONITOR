using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KOSHI.Api.Controllers;

[ApiController]
[Route("api/monitoring")]
public sealed class MonitoringController(MonitoringService monitoring) : ControllerBase
{
    [HttpGet("live")]
    [Authorize(Policy = Permissions.MonitoringLive)]
    public async Task<ActionResult<IReadOnlyList<LiveDeviceDto>>> Live([FromQuery] MonitoringQuery q, CancellationToken ct) =>
        Ok(await monitoring.LiveAsync(q, ct));

    [HttpGet("timeline")]
    [Authorize(Policy = Permissions.MonitoringRead)]
    public async Task<ActionResult<PagedResult<TimelineItemDto>>> Timeline([FromQuery] TimelineQuery q, CancellationToken ct) =>
        Ok(await monitoring.TimelineAsync(q, ct));

    [HttpGet("application-usage")]
    [Authorize(Policy = Permissions.MonitoringRead)]
    public async Task<ActionResult<IReadOnlyList<ApplicationUsageDto>>> ApplicationUsage([FromQuery] MonitoringQuery q, CancellationToken ct) =>
        Ok(await monitoring.ApplicationUsageAsync(q, ct));

    [HttpGet("idle-summary")]
    [Authorize(Policy = Permissions.ReportsRead)]
    public async Task<ActionResult<IdleSummaryDto>> IdleSummary([FromQuery] MonitoringQuery q, CancellationToken ct) =>
        Ok(await monitoring.IdleSummaryAsync(q, ct));

    [HttpGet("activity-detail")]
    [Authorize(Policy = Permissions.MonitoringRead)]
    public async Task<ActionResult<ActivityDetailDto>> ActivityDetail([FromQuery] MonitoringQuery q, CancellationToken ct) =>
        Ok(await monitoring.ActivityDetailAsync(q, ct));
}
