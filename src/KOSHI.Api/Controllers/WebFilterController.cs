using KOSHI.Api.Realtime;
using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace KOSHI.Api.Controllers;

/// <summary>Website blocking with timed exceptions, VPN blocking and software-installation lock. Every change is pushed to agents immediately.</summary>
[ApiController]
[Route("api/web-filter")]
public sealed class WebFilterController(WebFilterService webFilter, SettingsService settings, IHubContext<AgentHub> agents) : ControllerBase
{
    [HttpGet("sites")]
    [Authorize(Policy = Permissions.PoliciesRead)]
    public async Task<ActionResult<IReadOnlyList<BlockedSiteDto>>> Sites(CancellationToken ct) => Ok(await webFilter.ListAsync(ct));

    [HttpPost("sites")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<IReadOnlyList<BlockedSiteDto>>> Add(CreateBlockedSitesRequest request, CancellationToken ct)
    {
        var list = await webFilter.AddAsync(request, ct);
        await PushAsync(ct);
        return Ok(list);
    }

    [HttpPut("sites/{id:guid}")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<BlockedSiteDto>> Update(Guid id, UpdateBlockedSiteRequest request, CancellationToken ct)
    {
        var dto = await webFilter.UpdateAsync(id, request, ct);
        await PushAsync(ct);
        return Ok(dto);
    }

    [HttpDelete("sites/{id:guid}")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await webFilter.DeleteAsync(id, ct);
        await PushAsync(ct);
        return NoContent();
    }

    [HttpPost("sites/{id:guid}/allow")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<BlockedSiteDto>> Allow(Guid id, GrantSiteAllowanceRequest request, CancellationToken ct)
    {
        var dto = await webFilter.GrantAsync(id, request, ct);
        await PushAsync(ct);
        return Ok(dto);
    }

    [HttpDelete("allowances/{id:guid}")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<IActionResult> RevokeAllowance(Guid id, CancellationToken ct)
    {
        await webFilter.RevokeAllowanceAsync(id, ct);
        await PushAsync(ct);
        return NoContent();
    }

    [HttpGet("controls")]
    [Authorize(Policy = Permissions.PoliciesRead)]
    public async Task<ActionResult<DeviceControlsDto>> Controls(CancellationToken ct) => Ok(await settings.GetControlsAsync(ct));

    [HttpPut("controls")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<DeviceControlsDto>> UpdateControls(UpdateDeviceControlsRequest request, CancellationToken ct)
    {
        var dto = await settings.UpdateControlsAsync(request, ct);
        await PushAsync(ct);
        return Ok(dto);
    }

    private Task PushAsync(CancellationToken ct) =>
        agents.Clients.All.SendAsync("command", new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
}
