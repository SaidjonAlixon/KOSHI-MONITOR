using KOSHI.Domain;

namespace KOSHI.Application.Security;

/// <summary>
/// Permission catalogue. Every API endpoint is protected by one of these keys. Role → permission
/// assignments live in the database (Roles.Permissions) and are cached by <see cref="RolePermissionCache"/>;
/// <see cref="Defaults"/> is only used for seeding the built-in roles.
/// </summary>
public static class Permissions
{
    public const string DashboardRead = "dashboard.read";
    public const string DevicesRead = "devices.read";
    public const string DevicesManage = "devices.manage";
    public const string MonitoringRead = "monitoring.read";
    public const string MonitoringLive = "monitoring.live";
    public const string PoliciesRead = "policies.read";
    public const string PoliciesManage = "policies.manage";
    public const string ReportsRead = "reports.read";
    public const string ReportsIndividual = "reports.individual";
    public const string ReportsExport = "reports.export";
    public const string EmployeesRead = "employees.read";
    public const string EmployeesManage = "employees.manage";
    public const string AuditRead = "audit.read";
    public const string AlertsRead = "alerts.read";
    public const string AlertsManage = "alerts.manage";
    public const string UsersManage = "users.manage";
    public const string SettingsRead = "settings.read";
    public const string SettingsManage = "settings.manage";
    public const string SupportChat = "support.chat";

    /// <summary>Remote screen control and remote commands (lock, message, restart...). Always visible to the employee and audited.</summary>
    public const string RemoteControl = "remote.control";

    /// <summary>Holder can see all departments; others are restricted to User.DepartmentScopeId when set.</summary>
    public const string DataUnscoped = "data.unscoped";

    /// <summary>Editing roles and their permissions. Reserved for Super Admin and never assignable to other roles.</summary>
    public const string RolesManage = "roles.manage";

    public static readonly IReadOnlyList<string> All =
    [
        DashboardRead, DevicesRead, DevicesManage, MonitoringRead, MonitoringLive, PoliciesRead, PoliciesManage,
        ReportsRead, ReportsIndividual, ReportsExport, EmployeesRead, EmployeesManage, AuditRead, AlertsRead,
        AlertsManage, UsersManage, SettingsRead, SettingsManage, SupportChat, RemoteControl, DataUnscoped, RolesManage,
    ];

    public static readonly IReadOnlySet<string> NonAssignable = new HashSet<string>(StringComparer.Ordinal) { RolesManage };

    public static string GroupOf(string permission) => permission.Split('.')[0];

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Defaults =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [RoleNames.SuperAdmin] = All,
            [RoleNames.ITAdmin] =
            [
                DashboardRead, DevicesRead, DevicesManage, MonitoringRead, MonitoringLive, PoliciesRead,
                PoliciesManage, ReportsRead, ReportsIndividual, ReportsExport, EmployeesRead, AuditRead,
                AlertsRead, AlertsManage, SettingsRead, SupportChat, RemoteControl, DataUnscoped,
            ],
            [RoleNames.HRManager] =
            [
                DashboardRead, ReportsRead, ReportsIndividual, ReportsExport, EmployeesRead, EmployeesManage, SupportChat,
            ],
            [RoleNames.Viewer] = [DashboardRead, ReportsRead],
        };
}

/// <summary>
/// In-memory snapshot of role permissions, reloaded at startup, after every role change and periodically.
/// Super Admin always has every permission so that it cannot be locked out.
/// </summary>
public sealed class RolePermissionCache
{
    private volatile IReadOnlyDictionary<string, IReadOnlySet<string>> _map = Build(
        Permissions.Defaults.Select(kv => (kv.Key, (IEnumerable<string>)kv.Value)));

    public void Load(IEnumerable<(string Role, IEnumerable<string> Permissions)> roles) => _map = Build(roles);

    public bool Has(IEnumerable<string> roles, string permission) =>
        roles.Any(r => r == RoleNames.SuperAdmin || (_map.TryGetValue(r, out var set) && set.Contains(permission)));

    public IReadOnlyList<string> For(IEnumerable<string> roles)
    {
        var list = roles.ToList();
        if (list.Contains(RoleNames.SuperAdmin)) return Permissions.All;
        return list.Where(_map.ContainsKey).SelectMany(r => _map[r]).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Build(IEnumerable<(string Role, IEnumerable<string> Permissions)> roles) =>
        roles.ToDictionary(r => r.Role, r => (IReadOnlySet<string>)r.Permissions.Where(Permissions.All.Contains).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
}
