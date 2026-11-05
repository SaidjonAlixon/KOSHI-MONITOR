using KOSHI.Application.Abstractions;
using KOSHI.Application.Security;
using KOSHI.Application.Services;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KOSHI.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Applies pending migrations at startup. Recommended only for development/test; use migration bundles in production.</summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}

public sealed class BootstrapOptions
{
    public const string Section = "Bootstrap";
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminDisplayName { get; set; } = "KOSHI Super Admin";

    /// <summary>Creates departments/employees flagged IsDemo=true. Never enable in production.</summary>
    public bool SeedDemoData { get; set; }
}

public sealed class DatabaseInitializer(
    KoshiDbContext db,
    IPasswordHasher hasher,
    ISecretBox secrets,
    DatabaseOptions dbOptions,
    BootstrapOptions bootstrap,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (dbOptions.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(ct);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var systemRoles = await db.Roles.Where(r => r.IsSystem).ToListAsync(ct);
        var rolesChanged = false;
        foreach (var role in systemRoles)
        {
            if (!Permissions.Defaults.TryGetValue(role.Name, out var defaults)) continue;
            if (role.Permissions.Count == 0 || role.Name == RoleNames.SuperAdmin)
            {
                if (!role.Permissions.SequenceEqual(defaults))
                {
                    role.Permissions = [.. defaults];
                    role.UpdatedAt = now;
                    rolesChanged = true;
                }
            }
            else
            {
                // ITAdmin always gets every operational permission. Other built-in roles only pick up newly introduced keys.
                var grant = role.Name == RoleNames.ITAdmin
                    ? defaults
                    : (IReadOnlyList<string>)[Permissions.SupportChat, Permissions.LocationRead];
                var missing = grant.Where(p => defaults.Contains(p) && !role.Permissions.Contains(p)).ToList();
                if (missing.Count > 0)
                {
                    role.Permissions = [.. role.Permissions, .. missing];
                    role.UpdatedAt = now;
                    rolesChanged = true;
                }
            }
        }
        if (rolesChanged) await db.SaveChangesAsync(ct);

        var legacySupport = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingsService.Keys.SupportContact && s.Value.Contains("example.com"), ct);
        if (legacySupport is not null)
        {
            db.SystemSettings.Remove(legacySupport);
            await db.SaveChangesAsync(ct);
        }

        var legacyNotice = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingsService.Keys.MonitoringNotice && (
            s.Value.StartsWith("Bu korporativ kompyuter") || s.Value == "Korporativ kompyuter monitoringi faol."), ct);
        if (legacyNotice is not null)
        {
            legacyNotice.Value = SettingsService.Defaults.MonitoringNotice;
            legacyNotice.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        if (!await db.WorkSchedules.AnyAsync(ct))
        {
            db.WorkSchedules.Add(new WorkSchedule
            {
                Id = Guid.NewGuid(), Name = "Standart ish jadvali (Du–Ju 09:00–18:00)", TimeZoneId = "Asia/Tashkent",
                WorkDaysMask = WorkDays.MondayToFriday, WorkStart = new(9, 0), WorkEnd = new(18, 0),
                BreakStart = new(13, 0), BreakEnd = new(14, 0), IsDefault = true, CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Policies.AnyAsync(ct))
        {
            var policy = new Policy
            {
                Id = Guid.NewGuid(), Name = "Standart monitoring siyosati", Description = "Audit-only rejim, sarlavhalar maskalanadi. Admin tomonidan publish qilinmaguncha agentlarga yuborilmaydi.",
                Mode = PolicyMode.AuditOnly, EffectiveFrom = now, WindowTitleCapture = WindowTitleCapture.Masked, CreatedAt = now, UpdatedAt = now,
            };
            policy.Assignments.Add(new PolicyAssignment { Id = Guid.NewGuid(), PolicyId = policy.Id, Scope = PolicyAssignmentScope.AllDevices, CreatedAt = now });
            db.Policies.Add(policy);
            await db.SaveChangesAsync(ct);
        }

        await BootstrapAdminAsync(now, ct);
        if (bootstrap.SeedDemoData) await SeedDemoAsync(now, ct);
    }

    private async Task BootstrapAdminAsync(DateTime now, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct)) return;
        if (string.IsNullOrWhiteSpace(bootstrap.AdminEmail) || string.IsNullOrEmpty(bootstrap.AdminPassword))
        {
            logger.LogWarning("No users exist. Set KOSHI_Bootstrap__AdminEmail and KOSHI_Bootstrap__AdminPassword to create the first Super Admin.");
            return;
        }

        var errors = PasswordPolicy.Validate(bootstrap.AdminPassword, bootstrap.AdminEmail);
        if (errors.Count > 0)
        {
            logger.LogError("Bootstrap admin password does not satisfy the password policy: {Errors}", string.Join(" ", errors));
            return;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = bootstrap.AdminEmail.Trim().ToLowerInvariant(),
            NormalizedEmail = AuthService.NormalizeEmail(bootstrap.AdminEmail),
            DisplayName = bootstrap.AdminDisplayName,
            PasswordHash = hasher.Hash(bootstrap.AdminPassword),
            PasswordSecret = secrets.Seal(bootstrap.AdminPassword),
            CreatedAt = now,
            UpdatedAt = now,
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = KoshiDbContext.SuperAdminRoleId });
        db.Users.Add(user);
        db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), OccurredAt = now, ActorType = AuditActorType.System, Action = "user.bootstrap_admin", EntityType = "User", EntityId = user.Id.ToString() });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bootstrap Super Admin {UserId} created", user.Id);
    }

    private async Task SeedDemoAsync(DateTime now, CancellationToken ct)
    {
        if (await db.Departments.AnyAsync(d => d.IsDemo, ct)) return;
        var it = new Department { Id = Guid.NewGuid(), Name = "[DEMO] IT bo'limi", Branch = "Toshkent", IsDemo = true, CreatedAt = now };
        var hr = new Department { Id = Guid.NewGuid(), Name = "[DEMO] HR bo'limi", Branch = "Toshkent", IsDemo = true, CreatedAt = now };
        var fin = new Department { Id = Guid.NewGuid(), Name = "[DEMO] Moliya", Branch = "Samarqand", IsDemo = true, CreatedAt = now };
        db.Departments.AddRange(it, hr, fin);
        var employees = new (string Name, string Number, string Position, Department Dept)[]
        {
            ("[DEMO] Aziz Karimov", "DEMO-001", "Dasturchi", it),
            ("[DEMO] Dilnoza Rahimova", "DEMO-002", "Tizim administratori", it),
            ("[DEMO] Jasur Toshmatov", "DEMO-003", "HR mutaxassisi", hr),
            ("[DEMO] Malika Yusupova", "DEMO-004", "Buxgalter", fin),
        };
        foreach (var (name, number, position, dept) in employees)
            db.Employees.Add(new Employee { Id = Guid.NewGuid(), FullName = name, EmployeeNumber = number, Position = position, DepartmentId = dept.Id, IsDemo = true, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo organization data seeded (flagged IsDemo)");
    }
}
