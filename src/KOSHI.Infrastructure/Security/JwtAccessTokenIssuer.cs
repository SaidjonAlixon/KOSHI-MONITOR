using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using KOSHI.Application.Abstractions;
using KOSHI.Domain;
using Microsoft.IdentityModel.Tokens;

namespace KOSHI.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "koshi-api";
    public string Audience { get; set; } = "koshi-admin";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Must come from the environment (KOSHI_Jwt__SigningKey), never from source control.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;

    public SymmetricSecurityKey CreateKey()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        if (bytes.Length < 32) throw new InvalidOperationException("Jwt:SigningKey must be configured with at least 32 bytes (set KOSHI_Jwt__SigningKey).");
        return new SymmetricSecurityKey(bytes);
    }
}

public static class KoshiClaims
{
    public const string DepartmentScope = "koshi:dept";
}

public sealed class JwtAccessTokenIssuer(JwtOptions options, TimeProvider clock) : IAccessTokenIssuer
{
    private readonly SigningCredentials _credentials = new(options.CreateKey(), SecurityAlgorithms.HmacSha256);

    public (string Token, DateTime ExpiresAt) Issue(User user, IReadOnlyList<string> roles)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (user.DepartmentScopeId is { } d) claims.Add(new Claim(KoshiClaims.DepartmentScope, d.ToString()));

        var token = new JwtSecurityToken(options.Issuer, options.Audience, claims, now, expires, _credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
