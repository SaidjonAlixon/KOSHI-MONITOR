using KOSHI.Api.Security;
using KOSHI.Application.Common;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KOSHI.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth, IConfiguration config) : ControllerBase
{
    public const string RefreshCookie = "koshi_rt";
    public const string ClientHeader = "X-KOSHI-Client";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        SetRefreshCookie(result.RefreshToken);
        return Ok(result.Response);
    }

    /// <summary>Rotates the refresh token (httpOnly cookie) and returns a new access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Refresh(CancellationToken ct)
    {
        RequireClientHeader();
        var result = await auth.RefreshAsync(Request.Cookies[RefreshCookie], HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        SetRefreshCookie(result.RefreshToken);
        return Ok(result.Response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        RequireClientHeader();
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct) => Ok(await auth.MeAsync(User.UserId(), ct));

    /// <summary>The custom header forces a CORS preflight, so cross-site pages cannot trigger cookie-based refresh (CSRF).</summary>
    private void RequireClientHeader()
    {
        if (Request.Headers[ClientHeader] != "admin")
            throw Errors.Forbidden("Missing X-KOSHI-Client header.");
    }

    private void SetRefreshCookie(IssuedRefreshToken token) =>
        Response.Cookies.Append(RefreshCookie, token.Token, CookieOptions(token.ExpiresAt));

    private CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = config.GetValue("Auth:CookieSecure", true),
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires,
        IsEssential = true,
    };
}
