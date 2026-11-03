using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Application.Security;
using KOSHI.Contracts;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

public sealed class UserService(IKoshiDbContext db, IPasswordHasher hasher, AuditWriter audit, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<PagedResult<UserDto>> ListAsync(PageQuery q, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role).Where(u => u.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToUpperInvariant();
            query = query.Where(u => u.NormalizedEmail.Contains(s) || u.DisplayName.ToUpper().Contains(s));
        }
        query = (q.SortBy?.ToLowerInvariant(), q.SortDir) switch
        {
            ("email", "desc") => query.OrderByDescending(u => u.Email),
            ("email", _) => query.OrderBy(u => u.Email),
            ("lastloginat", "desc") => query.OrderByDescending(u => u.LastLoginAt),
            ("lastloginat", _) => query.OrderBy(u => u.LastLoginAt),
            (_, "desc") => query.OrderByDescending(u => u.DisplayName),
            _ => query.OrderBy(u => u.DisplayName),
        };
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        return new PagedResult<UserDto>(items.Select(ToDto).ToList(), q.Page, q.PageSize, total);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest r, CancellationToken ct = default)
    {
        var errors = PasswordPolicy.Validate(r.Password, r.Email);
        if (errors.Count > 0) throw Errors.Validation(new Dictionary<string, string[]> { [nameof(r.Password)] = errors.ToArray() });

        var normalized = AuthService.NormalizeEmail(r.Email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            throw Errors.Conflict("A user with this e-mail already exists.");

        var roles = await ResolveRolesAsync(r.Roles, ct);
        await EnsureDepartmentAsync(r.DepartmentScopeId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = r.Email.Trim().ToLowerInvariant(),
            NormalizedEmail = normalized,
            DisplayName = r.DisplayName.Trim(),
            PasswordHash = hasher.Hash(r.Password),
            DepartmentScopeId = r.DepartmentScopeId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        user.UserRoles = roles.Select(role => new UserRole { UserId = user.Id, RoleId = role.Id, Role = role }).ToList();
        db.Users.Add(user);
        audit.Record("user.create", "User", user.Id, new { roles = roles.Select(x => x.Name), user.DepartmentScopeId });
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest r, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null, ct) ?? throw Errors.NotFound("User");

        var roles = await ResolveRolesAsync(r.Roles, ct);
        await EnsureDepartmentAsync(r.DepartmentScopeId, ct);

        var wasSuperAdmin = user.UserRoles.Any(ur => ur.Role!.Name == RoleNames.SuperAdmin) && user.IsActive;
        var staysSuperAdmin = roles.Any(x => x.Name == RoleNames.SuperAdmin) && r.IsActive;
        if (wasSuperAdmin && !staysSuperAdmin)
        {
            var others = await db.UserRoles.CountAsync(ur => ur.Role!.Name == RoleNames.SuperAdmin && ur.UserId != id && ur.User!.IsActive && ur.User.DeletedAt == null, ct);
            if (others == 0) throw Errors.Conflict("The last active Super Admin cannot be demoted or deactivated.");
        }
        if (id == currentUser.UserId && !r.IsActive) throw Errors.Conflict("You cannot deactivate your own account.");

        var now = clock.GetUtcNow().UtcDateTime;
        var revokeSessions = false;
        if (!string.IsNullOrEmpty(r.NewPassword))
        {
            var errors = PasswordPolicy.Validate(r.NewPassword, user.Email);
            if (errors.Count > 0) throw Errors.Validation(new Dictionary<string, string[]> { [nameof(r.NewPassword)] = errors.ToArray() });
            user.PasswordHash = hasher.Hash(r.NewPassword);
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
            revokeSessions = true;
        }
        if (user.IsActive && !r.IsActive) revokeSessions = true;

        var oldRoles = user.UserRoles.Select(ur => ur.Role!.Name).OrderBy(x => x).ToList();
        user.DisplayName = r.DisplayName.Trim();
        user.IsActive = r.IsActive;
        user.DepartmentScopeId = r.DepartmentScopeId;
        user.UpdatedAt = now;
        db.UserRoles.RemoveRange(user.UserRoles.Where(ur => roles.All(x => x.Id != ur.RoleId)));
        foreach (var role in roles.Where(x => user.UserRoles.All(ur => ur.RoleId != x.Id)))
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, Role = role });

        if (revokeSessions)
        {
            var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
            foreach (var t in tokens) { t.RevokedAt = now; t.RevokedReason = "user_updated"; }
        }

        audit.Record("user.update", "User", user.Id, new
        {
            oldRoles,
            newRoles = roles.Select(x => x.Name).OrderBy(x => x),
            r.IsActive,
            passwordChanged = !string.IsNullOrEmpty(r.NewPassword),
            r.DepartmentScopeId,
        });
        await db.SaveChangesAsync(ct);

        var reloaded = await db.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstAsync(u => u.Id == id, ct);
        return ToDto(reloaded);
    }

    private async Task<List<Role>> ResolveRolesAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var requested = names.Distinct(StringComparer.Ordinal).ToList();
        var roles = await db.Roles.Where(r => requested.Contains(r.Name)).ToListAsync(ct);
        if (roles.Count != requested.Count) throw Errors.Validation("Roles", "One or more roles are unknown.");
        return roles;
    }

    private async Task EnsureDepartmentAsync(Guid? id, CancellationToken ct)
    {
        if (id is { } d && !await db.Departments.AnyAsync(x => x.Id == d, ct))
            throw Errors.Validation("DepartmentScopeId", "Department not found.");
    }

    private static UserDto ToDto(User u) => new(
        u.Id, u.Email, u.DisplayName, u.IsActive, u.MfaEnabled,
        u.UserRoles.Select(ur => ur.Role!.Name).OrderBy(x => x, StringComparer.Ordinal).ToList(),
        u.DepartmentScopeId, u.LastLoginAt, u.CreatedAt);
}
