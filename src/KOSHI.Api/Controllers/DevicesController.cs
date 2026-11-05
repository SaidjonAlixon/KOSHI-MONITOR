using KOSHI.Api.Realtime;
using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace KOSHI.Api.Controllers;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController(DeviceService devices, PolicyService policies, RemoteRegistry registry, IHubContext<AgentHub> agents, ICurrentUser user) : ControllerBase
{
    /// <summary>Agent enrollment with a one-time activation code. Returns the device credential once.</summary>
    [HttpPost("activate")]
    [AllowAnonymous]
    [EnableRateLimiting("activation")]
    public async Task<ActionResult<DeviceActivationResponse>> Activate(DeviceActivationRequest request, CancellationToken ct) =>
        Ok(await devices.ActivateAsync(request, ct));

    [HttpGet]
    [Authorize(Policy = Permissions.DevicesRead)]
    public async Task<ActionResult<PagedResult<DeviceDto>>> List([FromQuery] DeviceQuery query, CancellationToken ct) =>
        Ok(await devices.ListAsync(query, ct));

    /// <summary>Registers a device record and issues its first one-time activation code.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<ActivationCodeResponse>> Create(CreateDeviceRequest request, CancellationToken ct)
    {
        var result = await devices.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.DeviceId }, result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.DevicesRead)]
    public async Task<ActionResult<DeviceDto>> Get(Guid id, CancellationToken ct) => Ok(await devices.GetAsync(id, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<DeviceDto>> Update(Guid id, UpdateDeviceRequest request, CancellationToken ct) =>
        Ok(await devices.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/activation-code")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<ActivationCodeResponse>> IssueCode(Guid id, IssueActivationCodeRequest request, CancellationToken ct) =>
        Ok(await devices.IssueActivationCodeAsync(id, request.TtlMinutes, request.LicenseDays, ct));

    [HttpPost("{id:guid}/block")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<DeviceDto>> Block(Guid id, BlockDeviceRequest request, CancellationToken ct) =>
        Ok(await devices.BlockAsync(id, request.Reason, ct));

    [HttpPost("{id:guid}/unblock")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<DeviceDto>> Unblock(Guid id, CancellationToken ct) => Ok(await devices.UnblockAsync(id, ct));

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<DeviceDto>> Revoke(Guid id, CancellationToken ct)
    {
        var dto = await devices.RevokeAsync(id, ct);
        await agents.Clients.Group(AgentHub.Group(id)).SendAsync("command",
            new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return Ok(dto);
    }

    /// <summary>Extends the activation license or makes it permanent, then tells the agent to refresh.</summary>
    [HttpPut("{id:guid}/license")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<DeviceDto>> License(Guid id, SetDeviceLicenseRequest request, CancellationToken ct)
    {
        var dto = await devices.SetLicenseAsync(id, request.LicenseDays, ct);
        await agents.Clients.Group(AgentHub.Group(id)).SendAsync("command",
            new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return Ok(dto);
    }

    /// <summary>Turns the Windows-Settings lock ("Tizim himoyasi") on or off for this computer and pushes the change to the agent.</summary>
    [HttpPut("{id:guid}/protection")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<DeviceDto>> Protection(Guid id, SetDeviceProtectionRequest request, CancellationToken ct)
    {
        var dto = await devices.SetProtectionAsync(id, request.Enabled, ct);
        await agents.Clients.Group(AgentHub.Group(id)).SendAsync("command",
            new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return Ok(dto);
    }

    /// <summary>VPN and new-install lock for this computer only. Null follows the organization default.</summary>
    [HttpPut("{id:guid}/controls")]
    [Authorize(Policy = Permissions.PoliciesManage)]
    public async Task<ActionResult<DeviceDto>> NetworkControls(Guid id, SetDeviceNetworkControlsRequest request, CancellationToken ct)
    {
        var dto = await devices.SetNetworkControlsAsync(id, request.VpnBlocked, request.InstallBlocked, ct);
        await agents.Clients.Group(AgentHub.Group(id)).SendAsync("command",
            new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return Ok(dto);
    }

    /// <summary>Tells the online agent to remove KOSHI from that computer, then revokes the device credential.</summary>
    [HttpPost("{id:guid}/uninstall")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<DeviceDto>> Uninstall(Guid id, CancellationToken ct)
    {
        if (!registry.IsAgentConnected(id))
            throw Errors.Conflict("Agent onlayn emas. Kompyuter internetga ulanganida qayta urinib ko‘ring.");
        var device = await devices.GetAsync(id, ct);
        if (!Version.TryParse(device.AgentVersion, out var ver) || ver < new Version(1, 0, 22))
            throw Errors.Conflict("Bu kompyuterdagi agent hali 1.0.22 ga yangilanmagan. Agent o‘zi yangilanadi — bir necha daqiqa kuting, so‘ng qayta urinib ko‘ring.");
        var commandId = Guid.NewGuid();
        await agents.Clients.Group(AgentHub.Group(id)).SendAsync("command",
            new RemoteCommandDto(commandId, RemoteCommands.UninstallSelf, null, user.Email ?? "admin"), ct);
        return Ok(await devices.MarkRemoteUninstallAsync(id, ct));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await devices.DeleteAsync(id, ct);
        await agents.Clients.Group(AgentHub.Group(id)).SendAsync("command",
            new RemoteCommandDto(Guid.NewGuid(), RemoteCommands.RefreshPolicy, null, "KOSHI"), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/status")]
    [Authorize(Policy = Permissions.DevicesRead)]
    public async Task<ActionResult<DeviceStatusDto>> Status(Guid id, CancellationToken ct) => Ok(await devices.StatusAsync(id, ct));

    /// <summary>Exact online/offline session history for the device.</summary>
    [HttpGet("{id:guid}/connections")]
    [Authorize(Policy = Permissions.DevicesRead)]
    public async Task<ActionResult<IReadOnlyList<DeviceConnectionDto>>> Connections(Guid id, [FromQuery] int days = 7, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default) =>
        Ok(await devices.ConnectionsAsync(id, days, from, to, ct));

    [HttpGet("{id:guid}/policy-status")]
    [Authorize(Policy = Permissions.PoliciesRead)]
    public async Task<ActionResult<DevicePolicyStatusDto>> PolicyStatus(Guid id, CancellationToken ct) =>
        Ok(await policies.DevicePolicyStatusAsync(id, ct));
}
