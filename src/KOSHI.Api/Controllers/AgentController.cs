using KOSHI.Api.Realtime;
using KOSHI.Api.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace KOSHI.Api.Controllers;

/// <summary>Endpoints called by KOSHI agents, authenticated with a per-device credential.</summary>
[ApiController]
[Authorize(Policy = AuthSchemes.DevicePolicy)]
[EnableRateLimiting("device")]
public sealed class AgentController(DeviceService devices, TelemetryService telemetry, PolicyService policies) : ControllerBase
{
    [HttpPost("api/telemetry/batch")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ActionResult<TelemetryBatchResponse>> Batch(TelemetryBatchRequest request, CancellationToken ct) =>
        Ok(await telemetry.IngestAsync(User.DeviceId(), request, ct));

    [HttpPost("api/agent/heartbeat")]
    public async Task<ActionResult<HeartbeatResponse>> Heartbeat(HeartbeatRequest request, CancellationToken ct) =>
        Ok(await devices.HeartbeatAsync(User.DeviceId(), request, ct));

    [HttpGet("api/agent/policy")]
    public async Task<ActionResult<AgentPolicyDto>> Policy(CancellationToken ct) =>
        Ok(await policies.GetAgentPolicyAsync(User.DeviceId(), ct));

    [HttpPost("api/agent/policy-results")]
    public async Task<IActionResult> PolicyResult(PolicyResultReportRequest request, CancellationToken ct)
    {
        await policies.ReportResultAsync(User.DeviceId(), request, ct);
        return NoContent();
    }

    [HttpGet("api/agent/support")]
    public async Task<ActionResult<AgentSupportPollDto>> SupportPoll([FromServices] SupportService support, [FromServices] IHubContext<MonitoringHub> monitoring, [FromQuery] DateTime? since, CancellationToken ct)
    {
        var deviceId = User.DeviceId();
        var res = await support.PollForDeviceAsync(deviceId, since, ct);
        if (res.Messages.Any(m => m.Sender == "Admin"))
            await monitoring.Clients.All.SendAsync("supportMessagesRead", deviceId, ct);
        return Ok(res);
    }

    [HttpPost("api/agent/support")]
    public async Task<ActionResult<SupportMessageDto>> SupportSend([FromServices] SupportService support, [FromServices] IHubContext<MonitoringHub> monitoring, SendSupportMessageRequest request, CancellationToken ct)
    {
        var dto = await support.SendFromDeviceAsync(User.DeviceId(), request.Text, ct);
        await monitoring.Clients.All.SendAsync("supportMessage", dto, ct);
        return Ok(dto);
    }

    [HttpPost("api/agent/support/typing")]
    public async Task<IActionResult> SupportTyping([FromServices] IHubContext<MonitoringHub> monitoring, CancellationToken ct)
    {
        await monitoring.Clients.All.SendAsync("supportTyping", User.DeviceId(), ct);
        return NoContent();
    }
}
