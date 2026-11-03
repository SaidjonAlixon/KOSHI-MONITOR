using System.ComponentModel.DataAnnotations;

namespace KOSHI.Contracts;

// ---------- Devices ----------

public sealed class CreateDeviceRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    public Guid? EmployeeId { get; set; }

    [Range(5, 10_080)]
    public int ActivationCodeTtlMinutes { get; set; } = 60;
}

public sealed class UpdateDeviceRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    public Guid? EmployeeId { get; set; }
}

public sealed class BlockDeviceRequest
{
    [StringLength(500)]
    public string? Reason { get; set; }
}

public sealed class IssueActivationCodeRequest
{
    [Range(5, 10_080)]
    public int TtlMinutes { get; set; } = 60;
}

/// <summary>The activation code is shown once and never stored in plaintext.</summary>
public sealed record ActivationCodeResponse(Guid DeviceId, string ActivationCode, DateTime ExpiresAt);

public sealed record DeviceDto(
    Guid Id,
    string DisplayName,
    string? Hostname,
    string? OsVersion,
    string? AgentVersion,
    string Status,
    bool IsOnline,
    DateTime? LastHeartbeatAt,
    Guid? EmployeeId,
    string? EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    bool MonitoringEnabled,
    Guid? AppliedPolicyId,
    int? AppliedPolicyVersion,
    DateTime CreatedAt,
    DateTime? ActivatedAt,
    string? BlockReason);

public sealed class DeviceQuery : PageQuery
{
    [RegularExpression("^(Pending|Active|Blocked|Revoked)$")]
    public string? Status { get; set; }

    public bool? Online { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? EmployeeId { get; set; }
}

public sealed record DeviceStatusDto(
    Guid DeviceId,
    string Status,
    bool IsOnline,
    DateTime? LastHeartbeatAt,
    DateTime? ConnectedAt,
    DateTime? DisconnectedAt,
    bool MonitoringEnabled,
    string? CurrentProcessName,
    string? CurrentState,
    int? AppliedPolicyVersion,
    int RecentHeartbeatCount);

public sealed record DeviceConnectionDto(DateTime ConnectedAt, DateTime? DisconnectedAt, int? DurationSeconds);

// ---------- Employees / Departments ----------

public sealed record EmployeeDto(
    Guid Id,
    string FullName,
    string EmployeeNumber,
    string? Email,
    string? Position,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? WorkScheduleId,
    bool IsActive,
    bool IsDemo,
    int DeviceCount);

public sealed class UpsertEmployeeRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 1)]
    [RegularExpression("^[A-Za-z0-9_\\-]+$")]
    public string EmployeeNumber { get; set; } = string.Empty;

    [EmailAddress, StringLength(254)]
    public string? Email { get; set; }

    [StringLength(120)]
    public string? Position { get; set; }

    public Guid? DepartmentId { get; set; }
    public Guid? WorkScheduleId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class EmployeeQuery : PageQuery
{
    public Guid? DepartmentId { get; set; }
}

public sealed record DepartmentDto(Guid Id, string Name, string? Branch, int EmployeeCount, bool IsDemo);

public sealed class UpsertDepartmentRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(120)]
    public string? Branch { get; set; }
}

public sealed record WorkScheduleDto(
    Guid Id,
    string Name,
    string TimeZoneId,
    IReadOnlyList<DayOfWeek> WorkDays,
    TimeOnly WorkStart,
    TimeOnly WorkEnd,
    TimeOnly? BreakStart,
    TimeOnly? BreakEnd,
    bool IsDefault);

public sealed class UpsertWorkScheduleRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(64)]
    public string TimeZoneId { get; set; } = "Asia/Tashkent";

    [Required, MinLength(1), MaxLength(7)]
    public List<DayOfWeek> WorkDays { get; set; } = [];

    public TimeOnly WorkStart { get; set; }
    public TimeOnly WorkEnd { get; set; }
    public TimeOnly? BreakStart { get; set; }
    public TimeOnly? BreakEnd { get; set; }
}

// ---------- Policies / Applications ----------

public sealed record PolicyAssignmentDto(PolicyAssignmentScopeDto Scope, Guid? DepartmentId, Guid? DeviceId);

public sealed class UpsertPolicyRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    public PolicyModeDto Mode { get; set; } = PolicyModeDto.AuditOnly;

    [Range(1, 10_000)]
    public int Priority { get; set; } = 100;

    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public Guid? WorkScheduleId { get; set; }

    [Range(60, 7200)]
    public int IdleThresholdSeconds { get; set; } = 300;

    [Range(2, 5)]
    public int SamplingIntervalSeconds { get; set; } = 3;

    [Range(15, 600)]
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    public WindowTitleCaptureDto WindowTitleCapture { get; set; } = WindowTitleCaptureDto.Masked;
    public bool TrackBackgroundProcesses { get; set; } = true;

    [MaxLength(500)]
    public List<PolicyAssignmentDto> Assignments { get; set; } = [];
}

public sealed class PublishPolicyRequest
{
    [StringLength(500)]
    public string? Note { get; set; }

    /// <summary>Required to publish a policy in Enforce mode (explicit administrator approval).</summary>
    public bool ApproveEnforcement { get; set; }
}

public sealed class RollbackPolicyRequest
{
    /// <summary>Target published version; defaults to the previous published version.</summary>
    [Range(1, int.MaxValue)]
    public int? TargetVersion { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed record PolicyDto(
    Guid Id,
    string Name,
    string? Description,
    int Version,
    int? PublishedVersion,
    string Status,
    PolicyModeDto Mode,
    int Priority,
    bool EnforcementApproved,
    bool HasUnpublishedChanges,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    Guid? WorkScheduleId,
    int IdleThresholdSeconds,
    int SamplingIntervalSeconds,
    int HeartbeatIntervalSeconds,
    WindowTitleCaptureDto WindowTitleCapture,
    bool TrackBackgroundProcesses,
    bool ScreenshotsEnabled,
    IReadOnlyList<PolicyAssignmentDto> Assignments,
    IReadOnlyList<ApplicationRuleDto> Rules,
    IReadOnlyList<PolicyVersionDto> Versions,
    PolicyResultSummaryDto Results,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? PublishedAt);

public sealed record PolicyVersionDto(int Version, string? Note, DateTime CreatedAt, Guid? CreatedByUserId);

public sealed record PolicyResultSummaryDto(int Pending, int Applied, int Failed, int Unsupported, int RequiresAdministratorApproval);

public sealed record PolicyExecutionResultDto(
    Guid PolicyId,
    string PolicyName,
    int PolicyVersion,
    string Adapter,
    PolicyModeDto Mode,
    PolicyExecutionStatusDto Status,
    string? Message,
    DateTime UpdatedAt);

public sealed record DevicePolicyStatusDto(
    Guid DeviceId,
    Guid? EffectivePolicyId,
    string? EffectivePolicyName,
    int? EffectivePolicyVersion,
    int? AppliedPolicyVersion,
    IReadOnlyList<PolicyExecutionResultDto> Results);

public sealed record ApplicationRuleDto(
    Guid Id,
    Guid PolicyId,
    string Name,
    string ProcessName,
    string? PathPattern,
    string? Publisher,
    ApplicationClassificationDto Classification,
    ApplicationRestrictionDto Restriction,
    int? DailyLimitMinutes,
    bool RequirePassword,
    string? Description);

public sealed class UpsertApplicationRuleRequest
{
    [Required]
    public Guid PolicyId { get; set; }

    [Required, StringLength(120, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(AgentLimits.MaxProcessNameLength, MinimumLength = 1)]
    public string ProcessName { get; set; } = string.Empty;

    [StringLength(400)]
    public string? PathPattern { get; set; }

    [StringLength(400)]
    public string? Publisher { get; set; }

    public ApplicationClassificationDto Classification { get; set; }
    public ApplicationRestrictionDto Restriction { get; set; }

    [Range(1, 1440)]
    public int? DailyLimitMinutes { get; set; }

    /// <summary>Require a password to open this app on the agent.</summary>
    public bool RequirePassword { get; set; }

    /// <summary>New password to set (plaintext). Leave null to keep the existing one.</summary>
    [StringLength(128, MinimumLength = 4)]
    public string? Password { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }
}

// ---------- Support chat ----------

public sealed record SupportMessageDto(Guid Id, Guid DeviceId, string Sender, string? SenderName, string Text, DateTime CreatedAt, DateTime? ReadAt);

public sealed class SendSupportMessageRequest
{
    [Required, StringLength(2000, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;
}

public sealed record SupportThreadDto(Guid DeviceId, string DeviceName, string? EmployeeName, int UnreadFromDevice, DateTime? LastMessageAt, string? LastMessagePreview);

public sealed record AgentSupportPollDto(IReadOnlyList<SupportMessageDto> Messages, DateTime ServerTime);

public sealed record ApplicationSummaryDto(
    string ProcessName,
    long ForegroundSeconds,
    long BackgroundSeconds,
    int DeviceCount,
    string Classification);

// ---------- Monitoring ----------

public class MonitoringQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? DepartmentId { get; set; }

    [StringLength(120)]
    public string? Branch { get; set; }
}

public sealed class TimelineQuery : MonitoringQuery
{
    [Range(1, 100_000)]
    public int Page { get; set; } = 1;

    [Range(1, 500)]
    public int PageSize { get; set; } = 100;
}

public sealed record LiveDeviceDto(
    Guid DeviceId,
    string DeviceName,
    string? EmployeeName,
    string? DepartmentName,
    string? CurrentProcessName,
    string? CurrentWindowTitle,
    string? CurrentState,
    DateTime? ActivityStartedAt,
    int? ActivityDurationSeconds,
    DateTime? LastHeartbeatAt,
    DateTime? ConnectedAt,
    DateTime? DisconnectedAt,
    int? OnlineSeconds,
    string AgentStatus,
    bool MonitoringEnabled);

public sealed record TimelineItemDto(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    string Kind,
    string? ProcessName,
    string? WindowTitle,
    DateTime StartedAt,
    DateTime EndedAt,
    int DurationSeconds);

public sealed record ApplicationUsageDto(string ProcessName, long ForegroundSeconds, long BackgroundSeconds, int SessionCount, double Share);

public sealed record IdleSummaryDto(
    DateTime From,
    DateTime To,
    double ActiveSeconds,
    double IdleSeconds,
    double PausedSeconds,
    double ScheduledBreakSeconds,
    double OutsideScheduleSeconds,
    double UnknownSeconds,
    double ScheduledSeconds,
    int DeviceCount);

// ---------- Reports ----------

public sealed class ReportQuery : MonitoringQuery
{
    public DateOnly? Date { get; set; }

    [RegularExpression("^(device|employee|department)$")]
    public string? GroupBy { get; set; }
}

public sealed record ProductivityMetricDto(string Key, string Label, double? Value, double Weight, string Formula, string? MissingReason);

public sealed record ReportRowDto(
    string GroupKey,
    string GroupName,
    string? Position,
    string? DepartmentName,
    int DeviceCount,
    double ScheduledSeconds,
    double ActiveSeconds,
    double IdleSeconds,
    double PausedSeconds,
    double ScheduledBreakSeconds,
    double OutsideScheduleSeconds,
    double UnknownSeconds,
    double Coverage,
    string Confidence,
    double? ProductivityScore,
    bool IsReliable,
    IReadOnlyList<ProductivityMetricDto> Metrics,
    IReadOnlyList<ApplicationUsageDto> TopApplications);

public sealed record DailyActivityPointDto(DateOnly Date, double ActiveSeconds, double IdleSeconds, double UnknownSeconds);

public sealed record ReportDto(
    string Period,
    DateTime From,
    DateTime To,
    string TimeZone,
    string GroupBy,
    ProductivityFormulaDto Formula,
    IReadOnlyList<ReportRowDto> Rows,
    IReadOnlyList<DailyActivityPointDto> Daily,
    string Disclaimer);

public sealed record ProductivityFormulaDto(
    double ApprovedUsageWeight,
    double ScheduleAdherenceWeight,
    double ReportedActivityWeight,
    double MinimumCoverage,
    string Description);

// ---------- Audit / Alerts ----------

public sealed class AuditQuery : PageQuery
{
    [StringLength(100)]
    public string? Action { get; set; }

    public Guid? ActorUserId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed record AuditLogDto(
    Guid Id,
    DateTime OccurredAt,
    string ActorType,
    Guid? ActorUserId,
    string? ActorEmail,
    Guid? ActorDeviceId,
    string Action,
    string? EntityType,
    string? EntityId,
    bool Success,
    string? IpAddress,
    string? Details);

public sealed class AlertQuery : PageQuery
{
    public bool? Open { get; set; }

    [RegularExpression("^(DeviceOffline|PolicyFailed|PolicyViolation|TelemetryGap|Security)$")]
    public string? Type { get; set; }
}

public sealed record AlertDto(
    Guid Id,
    Guid? DeviceId,
    string? DeviceName,
    string Type,
    string Severity,
    string Message,
    DateTime CreatedAt,
    DateTime? AcknowledgedAt,
    DateTime? ResolvedAt);

// ---------- Users / Roles ----------

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    bool MfaEnabled,
    IReadOnlyList<string> Roles,
    Guid? DepartmentScopeId,
    DateTime? LastLoginAt,
    DateTime CreatedAt);

public sealed class CreateUserRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(120, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, StringLength(256, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(4)]
    public List<string> Roles { get; set; } = [];

    public Guid? DepartmentScopeId { get; set; }
}

public sealed class UpdateUserRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Required, MinLength(1), MaxLength(4)]
    public List<string> Roles { get; set; } = [];

    public Guid? DepartmentScopeId { get; set; }

    [StringLength(256, MinimumLength = 12)]
    public string? NewPassword { get; set; }
}

public sealed record RoleDto(Guid Id, string Name, string Description, IReadOnlyList<string> Permissions, bool IsSystem, int UserCount, bool PermissionsLocked);

public sealed class UpsertRoleRequest
{
    [Required, StringLength(50, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9_\\-]{2,49}$")]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(50)]
    public List<string> Permissions { get; set; } = [];
}

public sealed record PermissionInfoDto(string Key, string Group, bool Assignable);

// ---------- Settings / Dashboard / Health ----------

public sealed record SystemSettingsDto(
    int TelemetryRetentionDays,
    int HeartbeatRetentionDays,
    int AuditRetentionDays,
    int OfflineThresholdSeconds,
    string OrganizationTimeZone,
    string MonitoringNotice,
    string SupportContact,
    double ApprovedUsageWeight,
    double ScheduleAdherenceWeight,
    double ReportedActivityWeight,
    double MinimumCoverage,
    bool AgentAutoUpdate,
    bool AgentProtectionEnabled = false);

public sealed class UpdateSystemSettingsRequest
{
    [Range(7, 3650)]
    public int TelemetryRetentionDays { get; set; } = 90;

    [Range(1, 365)]
    public int HeartbeatRetentionDays { get; set; } = 30;

    [Range(30, 3650)]
    public int AuditRetentionDays { get; set; } = 365;

    [Range(60, 3600)]
    public int OfflineThresholdSeconds { get; set; } = 120;

    [Required, StringLength(64)]
    public string OrganizationTimeZone { get; set; } = "Asia/Tashkent";

    [Required, StringLength(2000, MinimumLength = 10)]
    public string MonitoringNotice { get; set; } = string.Empty;

    [StringLength(500)]
    public string SupportContact { get; set; } = string.Empty;

    [Range(0, 1)]
    public double ApprovedUsageWeight { get; set; } = 0.5;

    [Range(0, 1)]
    public double ScheduleAdherenceWeight { get; set; } = 0.3;

    [Range(0, 1)]
    public double ReportedActivityWeight { get; set; } = 0.2;

    [Range(0, 1)]
    public double MinimumCoverage { get; set; } = 0.5;

    /// <summary>Allow agents to download and install newer versions automatically.</summary>
    public bool AgentAutoUpdate { get; set; } = true;

    /// <summary>Require the admin protection password to open Windows Settings (blocks uninstall path) on agents.</summary>
    public bool AgentProtectionEnabled { get; set; }

    /// <summary>New protection password (plaintext). Leave null to keep the existing one.</summary>
    [StringLength(128, MinimumLength = 4)]
    public string? AgentProtectionPassword { get; set; }
}

public sealed record DashboardSummaryDto(
    int TotalDevices,
    int OnlineDevices,
    int OfflineDevices,
    int PendingDevices,
    int BlockedDevices,
    double MonitoredSecondsToday,
    int ActiveApplicationsToday,
    int PolicyFailures,
    int DevicesMissingData,
    int OpenAlerts,
    IReadOnlyList<DailyActivityPointDto> ActivityByDay,
    IReadOnlyList<ApplicationUsageDto> TopApplications,
    IReadOnlyList<DepartmentKpiDto> DepartmentKpis,
    DateTime GeneratedAt);

public sealed record DepartmentKpiDto(Guid? DepartmentId, string DepartmentName, double? ProductivityScore, double Coverage, double ActiveSeconds, double IdleSeconds);

public sealed record HealthDto(string Status, string Version, DateTime ServerTime, string Database);
