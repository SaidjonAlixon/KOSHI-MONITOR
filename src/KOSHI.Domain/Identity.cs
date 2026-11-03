namespace KOSHI.Domain;

public static class RoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string ITAdmin = "ITAdmin";
    public const string HRManager = "HRManager";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, ITAdmin, HRManager, Viewer];
}

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Reserved for TOTP/WebAuthn second factor; login flow already returns mfaRequired when set.</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>Optional data scope for HR Manager / Viewer: reports are restricted to this department.</summary>
    public Guid? DepartmentScopeId { get; set; }
    public Department? DepartmentScope { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<UserRole> UserRoles { get; set; } = [];
}

public sealed class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Permission keys granted to this role; editable by administrators with roles.manage.</summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>Built-in roles cannot be renamed or deleted.</summary>
    public bool IsSystem { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<UserRole> UserRoles { get; set; } = [];
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>All tokens produced by rotation from one login share a family; reuse of a rotated token revokes the family.</summary>
    public Guid FamilyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}
