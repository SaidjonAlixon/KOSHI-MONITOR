using System.Security.Claims;
using System.Text.Encodings.Web;
using KOSHI.Application.Abstractions;
using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Contracts;
using KOSHI.Domain;
using KOSHI.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace KOSHI.Api.Security;

public static class AuthSchemes
{
    public const string Admin = "AdminJwt";
    public const string Device = "DeviceCredential";
    public const string DevicePolicy = "device";
    public const string DeviceIdClaim = "koshi:device";
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>Resolves permissions from the DB-backed role cache at request time, so role edits apply immediately.</summary>
public sealed class PermissionHandler(RolePermissionCache cache) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);
        if (cache.Has(roles, requirement.Permission)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Authenticates agents with "Authorization: Device kd1.&lt;credentialId&gt;.&lt;secret&gt;".</summary>
public sealed class DeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DeviceService devices) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string FailureItem = "koshi:device-failure";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Device ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();

        var result = await devices.AuthenticateAsync(header["Device ".Length..].Trim(), Context.RequestAborted);
        if (result.Failure != DeviceAuthFailure.None)
        {
            Context.Items[FailureItem] = result.Failure;
            return AuthenticateResult.Fail(result.Failure == DeviceAuthFailure.Revoked ? "credential revoked" : "invalid credential");
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(AuthSchemes.DeviceIdClaim, result.DeviceId.ToString()),
            new Claim("koshi:device-status", result.Status.ToString()),
        ], AuthSchemes.Device);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), AuthSchemes.Device));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var revoked = Context.Items.TryGetValue(FailureItem, out var f) && f is DeviceAuthFailure.Revoked;
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Results.Problem(statusCode: 401, title: revoked ? "Device credential revoked" : "Device authentication required",
            extensions: new Dictionary<string, object?> { ["code"] = revoked ? ErrorCodes.CredentialRevoked : ErrorCodes.InvalidToken })
            .ExecuteAsync(Context);
    }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor, RolePermissionCache cache) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub"), out var id)
                           && Principal?.Identity?.AuthenticationType != AuthSchemes.Device ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email) ?? Principal?.FindFirstValue("email");

    public IReadOnlyList<string> Roles => Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public Guid? DepartmentScopeId => Guid.TryParse(Principal?.FindFirstValue(KoshiClaims.DepartmentScope), out var d) ? d : null;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool HasPermission(string permission) => cache.Has(Roles, permission);
}

public static class ClaimsPrincipalExtensions
{
    public static Guid DeviceId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(AuthSchemes.DeviceIdClaim), out var id) ? id : throw new UnauthorizedAccessException();

    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var id) ? id : throw new UnauthorizedAccessException();

    public static bool IsDeviceStatus(this ClaimsPrincipal user, DeviceStatus status) =>
        user.FindFirstValue("koshi:device-status") == status.ToString();
}
