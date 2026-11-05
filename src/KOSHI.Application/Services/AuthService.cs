using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Application.Security;
using KOSHI.Contracts;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KOSHI.Application.Services;

public sealed class AuthOptions
{
    public int RefreshTokenDays { get; set; } = 7;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public sealed record IssuedRefreshToken(string Token, DateTime ExpiresAt);

public sealed record AuthResult(LoginResponse Response, IssuedRefreshToken RefreshToken);

public sealed class AuthService(
    IKoshiDbContext db,
    IPasswordHasher hasher,
    ISecretBox secrets,
    IAccessTokenIssuer tokens,
    AuditWriter audit,
    TimeProvider clock,
    AuthOptions options,
    RolePermissionCache permissions,
    ILogger<AuthService> logger)
{
    // Verified against when the account does not exist so that response time does not reveal valid e-mails.
    private static string? _dummyHash;

    public async Task<AuthResult> LoginAsync(LoginRequest request, string? ip, CancellationToken ct = default)
    {
        var now = Now;
        var normalized = NormalizeEmail(request.Email);
        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized && u.DeletedAt == null, ct);

        if (user is null || !user.IsActive)
        {
            _dummyHash ??= hasher.Hash("dummy-password-for-timing");
            hasher.Verify(request.Password, _dummyHash, out _);
            audit.Record("auth.login", "User", user?.Id, new { email = normalized.ToLowerInvariant(), reason = user is null ? "unknown_user" : "inactive" }, success: false, actorType: AuditActorType.Anonymous);
            await db.SaveChangesAsync(ct);
            throw Errors.Unauthorized(ErrorCodes.InvalidCredentials, "Invalid e-mail or password.");
        }

        if (user.LockoutUntil is { } until && until > now)
        {
            audit.Record("auth.login", "User", user.Id, new { reason = "locked" }, success: false, actorType: AuditActorType.Anonymous);
            await db.SaveChangesAsync(ct);
            throw Errors.Unauthorized(ErrorCodes.AccountLocked, "Account is temporarily locked after repeated failed attempts. Try again later.");
        }

        if (!hasher.Verify(request.Password, user.PasswordHash, out var needsRehash))
        {
            user.FailedLoginCount++;
            var locked = false;
            if (user.FailedLoginCount >= options.MaxFailedLogins)
            {
                user.LockoutUntil = now.AddMinutes(options.LockoutMinutes);
                user.FailedLoginCount = 0;
                locked = true;
                logger.LogWarning("User {UserId} locked out after repeated failed logins", user.Id);
            }
            audit.Record("auth.login", "User", user.Id, new { reason = "bad_password", locked }, success: false, actorType: AuditActorType.Anonymous);
            await db.SaveChangesAsync(ct);
            throw Errors.Unauthorized(ErrorCodes.InvalidCredentials, "Invalid e-mail or password.");
        }

        if (needsRehash) user.PasswordHash = hasher.Hash(request.Password);
        user.PasswordSecret = secrets.Seal(request.Password);
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;

        var refresh = CreateRefreshToken(user.Id, Guid.NewGuid(), ip, now, out var refreshEntity);
        db.RefreshTokens.Add(refreshEntity);
        audit.Record("auth.login", "User", user.Id, success: true, actor: user);
        await db.SaveChangesAsync(ct);

        return new AuthResult(BuildLoginResponse(user), refresh);
    }

    public async Task<AuthResult> RefreshAsync(string? token, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200)
            throw Errors.Unauthorized(ErrorCodes.InvalidToken, "Refresh token is missing or invalid.");

        var now = Now;
        var hash = SecretUtil.Sha256Hex(token);
        var existing = await db.RefreshTokens
            .Include(t => t.User!).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null)
            throw Errors.Unauthorized(ErrorCodes.InvalidToken, "Refresh token is missing or invalid.");

        if (existing.RevokedAt is not null)
        {
            // A rotated token was presented again: assume theft and revoke the whole family.
            await RevokeFamilyAsync(existing.FamilyId, "reuse_detected", now, ct);
            audit.Record("auth.refresh_reuse_detected", "User", existing.UserId, success: false, actorType: AuditActorType.Anonymous);
            db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid(), Type = AlertType.Security, Severity = AlertSeverity.Critical, CreatedAt = now,
                Message = "Kirish tokeni qayta ishlatilgani aniqlandi; xavfsizlik uchun ushbu hisobning barcha sessiyalari bekor qilindi.",
                DedupKey = $"refresh-reuse:{existing.FamilyId}",
            });
            await db.SaveChangesAsync(ct);
            throw Errors.Unauthorized(ErrorCodes.InvalidToken, "Refresh token is no longer valid.");
        }

        var user = existing.User!;
        if (existing.ExpiresAt <= now || !user.IsActive || user.DeletedAt is not null)
        {
            existing.RevokedAt = now;
            existing.RevokedReason = "expired_or_inactive";
            await db.SaveChangesAsync(ct);
            throw Errors.Unauthorized(ErrorCodes.InvalidToken, "Refresh token is no longer valid.");
        }

        var refresh = CreateRefreshToken(user.Id, existing.FamilyId, ip, now, out var next);
        db.RefreshTokens.Add(next);
        existing.RevokedAt = now;
        existing.RevokedReason = "rotated";
        existing.ReplacedByTokenId = next.Id;
        await db.SaveChangesAsync(ct);

        return new AuthResult(BuildLoginResponse(user), refresh);
    }

    public async Task LogoutAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return;
        var hash = SecretUtil.Sha256Hex(token);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null) return;
        await RevokeFamilyAsync(existing.FamilyId, "logout", Now, ct);
        audit.Record("auth.logout", "User", existing.UserId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<CurrentUserDto> MeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null && u.IsActive, ct)
            ?? throw Errors.Unauthorized(ErrorCodes.InvalidToken, "User is not active.");
        return ToCurrentUser(user);
    }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public CurrentUserDto ToCurrentUser(User user)
    {
        var roles = user.UserRoles.Select(ur => ur.Role!.Name).OrderBy(r => r, StringComparer.Ordinal).ToList();
        return new CurrentUserDto(user.Id, user.Email, user.DisplayName, roles, permissions.For(roles), user.DepartmentScopeId);
    }

    internal async Task RevokeFamilyAsync(Guid familyId, string reason, DateTime now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active)
        {
            t.RevokedAt = now;
            t.RevokedReason = reason;
        }
    }

    private LoginResponse BuildLoginResponse(User user)
    {
        var current = ToCurrentUser(user);
        var (token, expires) = tokens.Issue(user, current.Roles);
        return new LoginResponse(token, expires, current, MfaRequired: false);
    }

    private IssuedRefreshToken CreateRefreshToken(Guid userId, Guid familyId, string? ip, DateTime now, out RefreshToken entity)
    {
        var raw = SecretUtil.RandomToken();
        entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = SecretUtil.Sha256Hex(raw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(options.RefreshTokenDays),
            CreatedByIp = ip,
        };
        return new IssuedRefreshToken(raw, entity.ExpiresAt);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
