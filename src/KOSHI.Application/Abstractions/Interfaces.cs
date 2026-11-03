using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KOSHI.Application.Abstractions;

public interface IKoshiDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Department> Departments { get; }
    DbSet<Employee> Employees { get; }
    DbSet<WorkSchedule> WorkSchedules { get; }
    DbSet<Device> Devices { get; }
    DbSet<DeviceActivation> DeviceActivations { get; }
    DbSet<DeviceCredential> DeviceCredentials { get; }
    DbSet<DeviceHeartbeat> DeviceHeartbeats { get; }
    DbSet<DeviceConnection> DeviceConnections { get; }
    DbSet<ApplicationEvent> ApplicationEvents { get; }
    DbSet<ActivitySession> ActivitySessions { get; }
    DbSet<ApplicationUsageSummary> ApplicationUsageSummaries { get; }
    DbSet<Policy> Policies { get; }
    DbSet<PolicyVersion> PolicyVersions { get; }
    DbSet<PolicyAssignment> PolicyAssignments { get; }
    DbSet<ApplicationRule> ApplicationRules { get; }
    DbSet<PolicyExecutionResult> PolicyExecutionResults { get; }
    DbSet<SupportMessage> SupportMessages { get; }
    DbSet<Alert> Alerts { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SystemSetting> SystemSettings { get; }

    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    IReadOnlyList<string> Roles { get; }
    Guid? DepartmentScopeId { get; }
    string? IpAddress { get; }
    bool HasPermission(string permission);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encodedHash, out bool needsRehash);
}

public interface IAccessTokenIssuer
{
    (string Token, DateTime ExpiresAt) Issue(User user, IReadOnlyList<string> roles);
}

/// <summary>Pushes "something changed" signals to connected admin dashboards (SignalR in the API).</summary>
public interface IMonitoringNotifier
{
    Task DeviceChangedAsync(Guid deviceId, CancellationToken ct = default);
}

/// <summary>Detects provider-specific unique-constraint violations without leaking provider types into services.</summary>
public interface IDbErrorClassifier
{
    bool IsUniqueViolation(Exception exception);
}
