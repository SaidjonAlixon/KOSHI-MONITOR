using KOSHI.Api.Realtime;
using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace KOSHI.Api.Controllers;

[ApiController]
[Route("api/policies")]
public sealed class PoliciesController(PolicyService policies, IHubContext<AgentHub> agents) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.PoliciesRead)]
    public async Task<ActionResult<IReadOnlyList<PolicyDto>>> List(CancellationToken ct) => Ok(await policies.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.PoliciesRead)]
    public async Task<ActionResult<PolicyDto>> Get(Guid id, CancellationToken ct) => Ok(await policies.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<PolicyDto>> Create(UpsertPolicyRequest request, CancellationToken ct)
    {
        var p = await policies.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = p.Id }, p);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<PolicyDto>> Update(Guid id, UpsertPolicyRequest request, CancellationToken ct) =>
        Ok(await policies.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<PolicyDto>> Publish(Guid id, PublishPolicyRequest request, CancellationToken ct)
    {
        var dto = await policies.PublishAsync(id, request, ct);
        await agents.Clients.All.SendAsync("command", new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return Ok(dto);
    }

    [HttpPost("{id:guid}/rollback")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<PolicyDto>> Rollback(Guid id, RollbackPolicyRequest request, CancellationToken ct)
    {
        var dto = await policies.RollbackAsync(id, request, ct);
        await agents.Clients.All.SendAsync("command", new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return Ok(dto);
    }
}

[ApiController]
[Route("api/applications")]
public sealed class ApplicationsController(PolicyService policies, MonitoringService monitoring, IHubContext<AgentHub> agents) : ControllerBase
{
    /// <summary>Applications observed in the period with their classification from policy rules.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.MonitoringRead)]
    public async Task<ActionResult<IReadOnlyList<ApplicationSummaryDto>>> List([FromQuery] MonitoringQuery q, CancellationToken ct)
    {
        q.From ??= DateTime.UtcNow.AddDays(-30);
        var usage = await monitoring.ApplicationUsageAsync(q, ct);
        var scope = await policies.GetScopeAsync(q.DeviceId, ct);
        var rules = await policies.ListRulesAsync(scope.PolicyId, ct);
        var byName = rules.GroupBy(r => r.ProcessName).ToDictionary(g => g.Key, g => g.First().Classification.ToString());
        return Ok(usage.Select(u => new ApplicationSummaryDto(u.ProcessName, u.ForegroundSeconds, u.BackgroundSeconds, 0,
            byName.GetValueOrDefault(u.ProcessName, "Unclassified"))).ToList());
    }

    [HttpGet("rules")]
    [Authorize(Policy = Permissions.PoliciesRead)]
    public async Task<ActionResult<IReadOnlyList<ApplicationRuleDto>>> Rules([FromQuery] Guid? policyId, CancellationToken ct) =>
        Ok(await policies.ListRulesAsync(policyId, ct));

    /// <summary>The policy edited for a target: no deviceId = all computers; a deviceId = its own policy if it has one.</summary>
    [HttpGet("scope")]
    [Authorize(Policy = Permissions.PoliciesRead)]
    public async Task<ActionResult<ApplicationScopeDto>> Scope([FromQuery] Guid? deviceId, CancellationToken ct) =>
        Ok(await policies.GetScopeAsync(deviceId, ct));

    /// <summary>Gives a computer its own rules (copied from the global policy) so they can be changed for it only.</summary>
    [HttpPost("scope")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<ApplicationScopeDto>> EnsureScope(EnsureDeviceScopeRequest request, CancellationToken ct)
    {
        var dto = await policies.EnsureDeviceScopeAsync(request.DeviceId, ct);
        await agents.Clients.All.SendAsync("command", new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return Ok(dto);
    }

    /// <summary>Removes the computer's own rules; it goes back to the global policy.</summary>
    [HttpDelete("scope/{deviceId:guid}")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<IActionResult> DeleteScope(Guid deviceId, CancellationToken ct)
    {
        await policies.DeleteDeviceScopeAsync(deviceId, ct);
        await agents.Clients.All.SendAsync("command", new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return NoContent();
    }

    [HttpPost("rules")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<ApplicationRuleDto>> CreateRule(UpsertApplicationRuleRequest request, CancellationToken ct)
    {
        var dto = await policies.CreateRuleAsync(request, ct);
        await agents.Clients.All.SendAsync("command", new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return StatusCode(201, dto);
    }

    [HttpPut("rules/{id:guid}")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<ApplicationRuleDto>> UpdateRule(Guid id, UpsertApplicationRuleRequest request, CancellationToken ct)
    {
        var dto = await policies.UpdateRuleAsync(id, request, ct);
        await agents.Clients.All.SendAsync("command", new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return Ok(dto);
    }

    [HttpDelete("rules/{id:guid}")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct)
    {
        await policies.DeleteRuleAsync(id, ct);
        await agents.Clients.All.SendAsync("command", new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return NoContent();
    }
}
