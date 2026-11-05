using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Application.Security;
using KOSHI.Contracts;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

public sealed class RoleService(IKoshiDbContext db, RolePermissionCache cache, AuditWriter audit, TimeProvider clock)
{
    public static IReadOnlyList<PermissionInfoDto> Catalog() =>
        Permissions.All.Select(p => new PermissionInfoDto(p, Permissions.GroupOf(p), !Permissions.NonAssignable.Contains(p))).ToList();

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        var roles = await db.Roles.AsNoTracking().OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new { Role = r, Users = r.UserRoles.Count(ur => ur.User!.DeletedAt == null) }).ToListAsync(ct);
        return roles.Select(x => ToDto(x.Role, x.Users)).ToList();
    }

    public async Task<RoleDto> CreateAsync(UpsertRoleRequest r, CancellationToken ct = default)
    {
        var name = r.Name.Trim();
        if (await db.Roles.AnyAsync(x => x.Name == name, ct)) throw Errors.Conflict("Bu nomli rol allaqachon mavjud.");
        var role = new Role
        {
            Id = Guid.NewGuid(), Name = name, Description = r.Description.Trim(), Permissions = ValidatePermissions(r.Permissions),
            IsSystem = false, UpdatedAt = Now,
        };
        db.Roles.Add(role);
        audit.Record("role.create", "Role", role.Id, new { role.Name, permissions = role.Permissions });
        await db.SaveChangesAsync(ct);
        await ReloadCacheAsync(db, cache, ct);
        return ToDto(role, 0);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, UpsertRoleRequest r, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Errors.NotFound("Rol");
        var name = r.Name.Trim();
        if (role.IsSystem && name != role.Name) throw Errors.Conflict("Tizim rollarini qayta nomlab bo‘lmaydi.");
        if (!role.IsSystem && name != role.Name && await db.Roles.AnyAsync(x => x.Name == name && x.Id != id, ct))
            throw Errors.Conflict("Bu nomli rol allaqachon mavjud.");

        var before = role.Permissions.ToList();
        role.Name = name;
        role.Description = r.Description.Trim();
        if (role.Name != RoleNames.SuperAdmin) role.Permissions = ValidatePermissions(r.Permissions);
        role.UpdatedAt = Now;

        audit.Record("role.update", "Role", id, new
        {
            role.Name,
            added = role.Permissions.Except(before),
            removed = before.Except(role.Permissions),
        });
        await db.SaveChangesAsync(ct);
        await ReloadCacheAsync(db, cache, ct);
        var users = await db.UserRoles.CountAsync(ur => ur.RoleId == id && ur.User!.DeletedAt == null, ct);
        return ToDto(role, users);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Errors.NotFound("Rol");
        if (role.Name == RoleNames.SuperAdmin) throw Errors.Conflict("Super Admin rolini o‘chirib bo‘lmaydi.");
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == id, ct)) throw Errors.Conflict("Avval bu rolni barcha foydalanuvchilardan olib tashlang, so‘ng o‘chiring.");
        db.Roles.Remove(role);
        audit.Record("role.delete", "Role", id, new { role.Name });
        await db.SaveChangesAsync(ct);
        await ReloadCacheAsync(db, cache, ct);
    }

    public static async Task ReloadCacheAsync(IKoshiDbContext db, RolePermissionCache cache, CancellationToken ct = default)
    {
        var roles = await db.Roles.AsNoTracking().Select(r => new { r.Name, r.Permissions }).ToListAsync(ct);
        cache.Load(roles.Select(r => (r.Name, (IEnumerable<string>)r.Permissions)));
    }

    private static List<string> ValidatePermissions(IEnumerable<string> requested)
    {
        var list = requested.Distinct(StringComparer.Ordinal).ToList();
        var unknown = list.Where(p => !Permissions.All.Contains(p)).ToList();
        if (unknown.Count > 0) throw Errors.Validation("Permissions", $"Noma’lum ruxsatlar: {string.Join(", ", unknown)}");
        if (list.Any(Permissions.NonAssignable.Contains)) throw Errors.Validation("Permissions", "roles.manage faqat Super Adminga tegishli va boshqa rollarga berilmaydi.");
        return list.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    private static RoleDto ToDto(Role r, int users) => new(
        r.Id, r.Name, r.Description,
        r.Name == RoleNames.SuperAdmin ? Permissions.All : r.Permissions,
        r.IsSystem, users, PermissionsLocked: r.Name == RoleNames.SuperAdmin);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
