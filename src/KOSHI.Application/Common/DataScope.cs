using KOSHI.Application.Abstractions;
using KOSHI.Application.Security;

namespace KOSHI.Application.Common;

public static class DataScope
{
    /// <summary>
    /// Returns the department filter that must be applied for the current user.
    /// Users without <see cref="Permissions.DataUnscoped"/> that have a department scope can only see that department.
    /// </summary>
    public static Guid? EffectiveDepartment(ICurrentUser user, Guid? requested)
    {
        if (user.HasPermission(Permissions.DataUnscoped) || user.DepartmentScopeId is null) return requested;
        if (requested is not null && requested != user.DepartmentScopeId)
            throw Errors.Forbidden("You can only access data of your assigned department.");
        return user.DepartmentScopeId;
    }
}
