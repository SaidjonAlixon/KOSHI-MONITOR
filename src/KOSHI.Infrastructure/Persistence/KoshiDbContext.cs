using KOSHI.Application.Abstractions;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Infrastructure.Persistence;

public sealed class KoshiDbContext(DbContextOptions<KoshiDbContext> options) : DbContext(options), IKoshiDbContext
{
    public static readonly Guid SuperAdminRoleId = Guid.Parse("8f1c7a52-0d7e-4b49-9a11-1f0000000001");
    public static readonly Guid ITAdminRoleId = Guid.Parse("8f1c7a52-0d7e-4b49-9a11-1f0000000002");
    public static readonly Guid HRManagerRoleId = Guid.Parse("8f1c7a52-0d7e-4b49-9a11-1f0000000003");
    public static readonly Guid ViewerRoleId = Guid.Parse("8f1c7a52-0d7e-4b49-9a11-1f0000000004");

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceActivation> DeviceActivations => Set<DeviceActivation>();
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
    public DbSet<DeviceHeartbeat> DeviceHeartbeats => Set<DeviceHeartbeat>();
    public DbSet<DeviceConnection> DeviceConnections => Set<DeviceConnection>();
    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();
    public DbSet<ActivitySession> ActivitySessions => Set<ActivitySession>();
    public DbSet<ApplicationUsageSummary> ApplicationUsageSummaries => Set<ApplicationUsageSummary>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<PolicyVersion> PolicyVersions => Set<PolicyVersion>();
    public DbSet<PolicyAssignment> PolicyAssignments => Set<PolicyAssignment>();
    public DbSet<ApplicationRule> ApplicationRules => Set<ApplicationRule>();
    public DbSet<PolicyExecutionResult> PolicyExecutionResults => Set<PolicyExecutionResult>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
            e.HasOne(x => x.DepartmentScope).WithMany().HasForeignKey(x => x.DepartmentScopeId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Role>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.Description).HasMaxLength(300);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Permissions).Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode(StringComparison.Ordinal))),
                v => v.ToList()));
            // Permissions of built-in roles are seeded at runtime by DatabaseInitializer.
            e.HasData(
                new Role { Id = SuperAdminRoleId, Name = RoleNames.SuperAdmin, IsSystem = true, Description = "Barcha tizim sozlamalari, administratorlar va rollar, qurilma va siyosatlar ustidan to'liq vakolat." },
                new Role { Id = ITAdminRoleId, Name = RoleNames.ITAdmin, IsSystem = true, Description = "Qurilmalar, agent holati, policy'lar, texnik diagnostika va cheklovlar." },
                new Role { Id = HRManagerRoleId, Name = RoleNames.HRManager, IsSystem = true, Description = "Xodimlar, ish vaqti statistikasi va umumlashtirilgan hisobotlar." },
                new Role { Id = ViewerRoleId, Name = RoleNames.Viewer, IsSystem = true, Description = "Faqat o'qish: cheklangan dashboard va bo'lim darajasidagi hisobotlar." });
        });

        b.Entity<UserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany(x => x.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.RevokedReason).HasMaxLength(50);
            e.Property(x => x.CreatedByIp).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Department>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Branch).HasMaxLength(120);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Employee>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.EmployeeNumber).HasMaxLength(50).IsRequired();
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Position).HasMaxLength(120);
            e.HasIndex(x => x.EmployeeNumber).IsUnique();
            e.HasIndex(x => x.DepartmentId);
            e.HasOne(x => x.Department).WithMany(x => x.Employees).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.WorkSchedule).WithMany().HasForeignKey(x => x.WorkScheduleId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<WorkSchedule>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        });

        b.Entity<Device>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Hostname).HasMaxLength(255);
            e.Property(x => x.OsVersion).HasMaxLength(255);
            e.Property(x => x.AgentVersion).HasMaxLength(50);
            e.Property(x => x.BlockReason).HasMaxLength(500);
            e.Property(x => x.CurrentProcessName).HasMaxLength(260);
            e.Property(x => x.CurrentWindowTitle).HasMaxLength(512);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.EmployeeId);
            e.HasIndex(x => x.LastHeartbeatAt);
            e.HasOne(x => x.Employee).WithMany(x => x.Devices).HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DeviceActivation>(e =>
        {
            e.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.UsedFromIp).HasMaxLength(64);
            e.HasIndex(x => x.CodeHash).IsUnique();
            e.HasIndex(x => x.DeviceId);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DeviceCredential>(e =>
        {
            e.Property(x => x.SecretHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.DeviceId);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DeviceHeartbeat>(e =>
        {
            e.Property(x => x.AgentVersion).HasMaxLength(50);
            e.HasIndex(x => new { x.DeviceId, x.ReceivedAt });
            e.HasIndex(x => x.ReceivedAt);
            e.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DeviceConnection>(e =>
        {
            e.Property(x => x.ConnectionId).HasMaxLength(64);
            e.HasIndex(x => new { x.DeviceId, x.ConnectedAt });
            e.HasIndex(x => x.DisconnectedAt);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ApplicationEvent>(e =>
        {
            e.Property(x => x.ProcessName).HasMaxLength(260).IsRequired();
            e.Property(x => x.WindowTitle).HasMaxLength(512);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.Metadata).HasColumnType("jsonb");
            e.HasIndex(x => new { x.DeviceId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.DeviceId, x.StartedAt });
            e.HasIndex(x => x.EndedAt);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ActivitySession>(e =>
        {
            e.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.DeviceId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.DeviceId, x.StartedAt });
            e.HasIndex(x => x.EndedAt);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ApplicationUsageSummary>(e =>
        {
            e.Property(x => x.ProcessName).HasMaxLength(260).IsRequired();
            e.HasIndex(x => new { x.DeviceId, x.Date, x.ProcessName }).IsUnique();
            e.HasIndex(x => x.Date);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Policy>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Name);
            e.HasOne(x => x.WorkSchedule).WithMany().HasForeignKey(x => x.WorkScheduleId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t => t.HasCheckConstraint("CK_Policies_NoScreenshots", "\"ScreenshotsEnabled\" = false"));
        });

        b.Entity<PolicyVersion>(e =>
        {
            e.Property(x => x.Snapshot).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.PolicyId, x.Version }).IsUnique();
            e.HasOne(x => x.Policy).WithMany(x => x.Versions).HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PolicyAssignment>(e =>
        {
            e.HasIndex(x => x.PolicyId);
            e.HasOne(x => x.Policy).WithMany(x => x.Assignments).HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_PolicyAssignments_Target",
                "(\"Scope\" = 0 AND \"DepartmentId\" IS NULL AND \"DeviceId\" IS NULL) OR " +
                "(\"Scope\" = 1 AND \"DepartmentId\" IS NOT NULL AND \"DeviceId\" IS NULL) OR " +
                "(\"Scope\" = 2 AND \"DeviceId\" IS NOT NULL AND \"DepartmentId\" IS NULL)"));
        });

        b.Entity<ApplicationRule>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.ProcessName).HasMaxLength(260).IsRequired();
            e.Property(x => x.PathPattern).HasMaxLength(400);
            e.Property(x => x.Publisher).HasMaxLength(400);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.PolicyId, x.ProcessName }).IsUnique();
            e.HasOne(x => x.Policy).WithMany(x => x.Rules).HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PolicyExecutionResult>(e =>
        {
            e.Property(x => x.Adapter).HasMaxLength(50).IsRequired();
            e.Property(x => x.Message).HasMaxLength(1000);
            e.HasIndex(x => new { x.DeviceId, x.PolicyId, x.PolicyVersion }).IsUnique();
            e.HasIndex(x => new { x.PolicyId, x.PolicyVersion, x.Status });
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Policy).WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Alert>(e =>
        {
            e.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            e.Property(x => x.DedupKey).HasMaxLength(200);
            e.HasIndex(x => x.DedupKey);
            e.HasIndex(x => new { x.ResolvedAt, x.CreatedAt });
            e.HasIndex(x => x.DeviceId);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActorEmail).HasMaxLength(254);
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.Action);
            e.HasIndex(x => x.ActorUserId);
        });

        b.Entity<SystemSetting>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.Property(x => x.Value).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
        });
    }
}
