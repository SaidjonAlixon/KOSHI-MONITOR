using KOSHI.Application.Security;
using KOSHI.Contracts;
using KOSHI.Infrastructure.Agent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KOSHI.Api.Controllers;

/// <summary>
/// Agent release distribution. Download endpoints are public (installer + metadata); publishing a new
/// version requires settings.manage. Agents poll /latest.json and auto-update to /download.
/// </summary>
[ApiController]
[Route("api/agent/release")]
public sealed class AgentReleaseController(AgentReleaseService releases) : ControllerBase
{
    [HttpGet("latest.json")]
    [AllowAnonymous]
    public async Task<IActionResult> Latest(CancellationToken ct)
    {
        var latest = await releases.GetLatestAsync(ct);
        return latest is null ? NotFound() : Ok(latest);
    }

    [HttpGet("download")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(CancellationToken ct)
    {
        var path = await releases.GetInstallerPathAsync(ct);
        if (path is null) return NotFound();
        return PhysicalFile(path, "application/octet-stream", "KOSHI-Agent-Setup.exe", enableRangeProcessing: true);
    }

    /// <summary>Current release info for the admin UI.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.SettingsRead)]
    public async Task<ActionResult<AgentReleaseDto>> Current(CancellationToken ct)
    {
        var latest = await releases.GetLatestAsync(ct);
        return latest is null ? NoContent() : Ok(latest);
    }

    /// <summary>Publish a new agent version by uploading the installer EXE. Agents auto-update afterwards.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.SettingsManage)]
    [RequestSizeLimit(300_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 300_000_000)]
    public async Task<ActionResult<AgentReleaseDto>> Publish([FromForm] string version, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { code = ErrorCodes.ValidationFailed, title = "Fayl tanlanmagan." });
        await using var stream = file.OpenReadStream();
        var dto = await releases.UploadAsync(stream, version, file.FileName, ct);
        return Ok(dto);
    }
}
