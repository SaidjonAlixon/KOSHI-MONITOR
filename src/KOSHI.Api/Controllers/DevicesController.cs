using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KOSHI.Api.Controllers;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController(DeviceService devices, PolicyService policies) : ControllerBase
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
        Ok(await devices.IssueActivationCodeAsync(id, request.TtlMinutes, ct));

    [HttpPost("{id:guid}/block")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<DeviceDto>> Block(Guid id, BlockDeviceRequest request, CancellationToken ct) =>
        Ok(await devices.BlockAsync(id, request.Reason, ct));

    [HttpPost("{id:guid}/unblock")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<DeviceDto>> Unblock(Guid id, CancellationToken ct) => Ok(await devices.UnblockAsync(id, ct));

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<ActionResult<DeviceDto>> Revoke(Guid id, CancellationToken ct) => Ok(await devices.RevokeAsync(id, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.DevicesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await devices.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/status")]
    [Authorize(Policy = Permissions.DevicesRead)]
    public async Task<ActionResult<DeviceStatusDto>> Status(Guid id, CancellationToken ct) => Ok(await devices.StatusAsync(id, ct));

    /// <summary>Exact online/offline session history for the device.</summary>
    [HttpGet("{id:guid}/connections")]
    [Authorize(Policy = Permissions.DevicesRead)]
    public async Task<ActionResult<IReadOnlyList<DeviceConnectionDto>>> Connections(Guid id, [FromQuery] int days = 7, CancellationToken ct = default) =>
        Ok(await devices.ConnectionsAsync(id, days, ct));

    [HttpGet("{id:guid}/policy-status")]
    [Authorize(Policy = Permissions.PoliciesRead)]
    public async Task<ActionResult<DevicePolicyStatusDto>> PolicyStatus(Guid id, CancellationToken ct) =>
        Ok(await policies.DevicePolicyStatusAsync(id, ct));
}
