namespace KOSHI.Domain;

public sealed class Policy
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Incremented on every publish; the published content is frozen in PolicyVersions.</summary>
    public int Version { get; set; }
    public int? PublishedVersion { get; set; }
    public PolicyStatus Status { get; set; } = PolicyStatus.Draft;
    public PolicyMode Mode { get; set; } = PolicyMode.AuditOnly;

    /// <summary>Lower number wins when several policies of the same scope target a device.</summary>
    public int Priority { get; set; } = 100;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public Guid? WorkScheduleId { get; set; }
    public WorkSchedule? WorkSchedule { get; set; }
    public int IdleThresholdSeconds { get; set; } = 300;
    public int SamplingIntervalSeconds { get; set; } = 3;
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public WindowTitleCapture WindowTitleCapture { get; set; } = WindowTitleCapture.Masked;
    public bool TrackBackgroundProcesses { get; set; } = true;

    /// <summary>Screenshots are not implemented in the MVP and must stay disabled.</summary>
    public bool ScreenshotsEnabled { get; set; }
    public bool EnforcementApproved { get; set; }
    public Guid? EnforcementApprovedByUserId { get; set; }
    public bool HasUnpublishedChanges { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<PolicyAssignment> Assignments { get; set; } = [];
    public List<ApplicationRule> Rules { get; set; } = [];
    public List<PolicyVersion> Versions { get; set; } = [];
}

public sealed class PolicyVersion
{
    public Guid Id { get; set; }
    public Guid PolicyId { get; set; }
    public Policy? Policy { get; set; }
    public int Version { get; set; }

    /// <summary>Immutable JSON snapshot of the policy content (settings + rules) sent to agents.</summary>
    public string Snapshot { get; set; } = "{}";
    public string? Note { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PolicyAssignment
{
    public Guid Id { get; set; }
    public Guid PolicyId { get; set; }
    public Policy? Policy { get; set; }
    public PolicyAssignmentScope Scope { get; set; }
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ApplicationRule
{
    public Guid Id { get; set; }
    public Guid PolicyId { get; set; }
    public Policy? Policy { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Executable file name, e.g. "chrome.exe" (case-insensitive).</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Optional AppLocker path condition, e.g. "%PROGRAMFILES%\Game\*".</summary>
    public string? PathPattern { get; set; }

    /// <summary>Optional AppLocker publisher condition (certificate subject, e.g. "O=VENDOR LTD").</summary>
    public string? Publisher { get; set; }
    public ApplicationClassification Classification { get; set; }
    public ApplicationRestriction Restriction { get; set; }

    /// <summary>Optional daily foreground-time limit; the agent reports a violation when exceeded.</summary>
    public int? DailyLimitMinutes { get; set; }

    /// <summary>When true, the agent asks for a password before letting this app run; wrong/cancel closes it.</summary>
    public bool RequirePassword { get; set; }
    public string? PasswordSalt { get; set; }
    public string? PasswordHash { get; set; }

    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class PolicyExecutionResult
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public Guid PolicyId { get; set; }
    public Policy? Policy { get; set; }
    public int PolicyVersion { get; set; }
    public string Adapter { get; set; } = string.Empty;
    public PolicyMode Mode { get; set; }
    public PolicyExecutionStatus Status { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
