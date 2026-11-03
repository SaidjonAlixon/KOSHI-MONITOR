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
        var unseededRoles = await db.Roles.Where(r => r.IsSystem && r.Permissions.Count == 0).ToListAsync(ct);
        foreach (var role in unseededRoles)
        {
            if (!Permissions.Defaults.TryGetValue(role.Name, out var defaults)) continue;
            role.Permissions = [.. defaults];
            role.UpdatedAt = now;
        }
        if (unseededRoles.Count > 0) await db.SaveChangesAsync(ct);

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
