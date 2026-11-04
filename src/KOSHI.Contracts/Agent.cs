using System.ComponentModel.DataAnnotations;

namespace KOSHI.Contracts;

public static class AgentLimits
{
    public const int MaxEventsPerBatch = 500;
    public const int MaxSessionsPerBatch = 500;
    public const int MaxProcessNameLength = 260;
    public const int MaxWindowTitleLength = 512;
    public const int MaxIdempotencyKeyLength = 100;
    public const int MaxEventDurationSeconds = 24 * 3600;
}

public sealed class DeviceActivationRequest
{
    [Required, StringLength(64, MinimumLength = 8)]
    public string ActivationCode { get; set; } = string.Empty;

    [Required, StringLength(255)]
    public string Hostname { get; set; } = string.Empty;

    [StringLength(255)]
    public string? OsVersion { get; set; }

    [StringLength(50)]
    public string? AgentVersion { get; set; }
}

/// <summary>The credential is returned exactly once; the agent must store it protected (DPAPI).</summary>
public sealed record DeviceActivationResponse(Guid DeviceId, string DeviceCredential, string DisplayName);

public sealed class HeartbeatRequest
{
    [StringLength(50)]
    public string? AgentVersion { get; set; }

    [StringLength(255)]
    public string? OsVersion { get; set; }

    public DateTime? ClientTime { get; set; }
    public bool MonitoringEnabled { get; set; }

    [Range(0, 1_000_000)]
    public int QueueLength { get; set; }

    public int? PolicyVersion { get; set; }
    public Guid? PolicyId { get; set; }

    [StringLength(AgentLimits.MaxProcessNameLength)]
    public string? CurrentProcessName { get; set; }

    [StringLength(AgentLimits.MaxWindowTitleLength)]
    public string? CurrentWindowTitle { get; set; }

    public ActivityStateDto? CurrentState { get; set; }
    public DateTime? CurrentActivityStartedAt { get; set; }
}

public sealed record HeartbeatResponse(
    DateTime ServerTime,
    bool MonitoringAllowed,
    Guid? PolicyId,
    int? PolicyVersion);

public sealed class TelemetryBatchRequest
{
    [Required]
    public Guid BatchId { get; set; }

    [StringLength(50)]
    public string? AgentVersion { get; set; }

    [MaxLength(AgentLimits.MaxEventsPerBatch)]
    public List<ApplicationEventDto> ApplicationEvents { get; set; } = [];

    [MaxLength(AgentLimits.MaxSessionsPerBatch)]
    public List<ActivitySessionDto> ActivitySessions { get; set; } = [];
}

public sealed class ApplicationEventDto
{
    [Required, StringLength(AgentLimits.MaxIdempotencyKeyLength, MinimumLength = 8)]
    [RegularExpression("^[A-Za-z0-9_:\\-\\.]+$")]
    public string IdempotencyKey { get; set; } = string.Empty;

    public ApplicationEventTypeDto EventType { get; set; }

    [Required, StringLength(AgentLimits.MaxProcessNameLength, MinimumLength = 1)]
    public string ProcessName { get; set; } = string.Empty;

    [StringLength(AgentLimits.MaxProcessNameLength)]
    public string? ExecutableName { get; set; }

    [StringLength(AgentLimits.MaxWindowTitleLength)]
    public string? WindowTitle { get; set; }

    [Range(0, int.MaxValue)]
    public int? ProcessId { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
}

public sealed class ActivitySessionDto
{
    [Required, StringLength(AgentLimits.MaxIdempotencyKeyLength, MinimumLength = 8)]
    [RegularExpression("^[A-Za-z0-9_:\\-\\.]+$")]
    public string IdempotencyKey { get; set; } = string.Empty;

    public ActivityStateDto State { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
}

public sealed record TelemetryBatchResponse(
    Guid BatchId,
    int Accepted,
    int Duplicates,
    int Rejected,
    IReadOnlyList<string> Errors);

/// <summary>Effective policy delivered to the agent. Version is the published version number.</summary>
public sealed record AgentPolicyDto(
    Guid? PolicyId,
    string? Name,
    int? Version,
    PolicyModeDto Mode,
    bool EnforcementApproved,
    int SamplingIntervalSeconds,
    int IdleThresholdSeconds,
    int HeartbeatIntervalSeconds,
    WindowTitleCaptureDto WindowTitleCapture,
    bool TrackBackgroundProcesses,
    bool ScreenshotsEnabled,
    bool AutoUpdateEnabled,
    WorkScheduleDto? WorkSchedule,
    IReadOnlyList<AgentApplicationRuleDto> Rules,
    string MonitoringNotice,
    string SupportContact,
    bool ProtectionEnabled = false,
    string? ProtectionSalt = null,
    string? ProtectionHash = null,
    bool LocationEnabled = false,
    int LocationIntervalSeconds = 300,
    IReadOnlyList<AgentBlockedSiteDto>? BlockedSites = null,
    bool VpnBlocked = false,
    bool InstallBlocked = false);

/// <summary>A blocked website (domain and all subdomains). Access is allowed only while <see cref="AllowedUntil"/> is in the future.</summary>
public sealed record AgentBlockedSiteDto(string Domain, DateTime? AllowedUntil);

/// <summary>Device position. Source: gps / wifi / cell / windows (Windows Location service) or ip (approximate, by public IP).</summary>
public sealed class DeviceLocationReportRequest
{
    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }

    [Range(0, 5_000_000)]
    public double? AccuracyMeters { get; set; }

    [Required, RegularExpression("^(gps|wifi|cell|windows|ip)$")]
    public string Source { get; set; } = "windows";

    [StringLength(120)]
    public string? City { get; set; }

    [StringLength(120)]
    public string? Region { get; set; }

    [StringLength(120)]
    public string? Country { get; set; }

    [StringLength(200)]
    public string? Isp { get; set; }

    public DateTime? CapturedAt { get; set; }
}

public sealed record AgentReleaseDto(
    string Version,
    string BuildDate,
    string FileName,
    long Size,
    string Sha256,
    DateTime UpdatedAt,
    string Source);

public sealed record AgentApplicationRuleDto(
    Guid Id,
    string Name,
    string ProcessName,
    string? PathPattern,
    string? Publisher,
    ApplicationClassificationDto Classification,
    ApplicationRestrictionDto Restriction,
    int? DailyLimitMinutes = null,
    bool RequirePassword = false,
    string? PasswordSalt = null,
    string? PasswordHash = null);

public sealed class PolicyResultReportRequest
{
    [Required]
    public Guid PolicyId { get; set; }

    [Range(1, int.MaxValue)]
    public int PolicyVersion { get; set; }

    [Required, StringLength(50, MinimumLength = 1)]
    [RegularExpression("^[A-Za-z0-9_\\-]+$")]
    public string Adapter { get; set; } = string.Empty;

    public PolicyModeDto Mode { get; set; }
    public PolicyExecutionStatusDto Status { get; set; }

    [StringLength(1000)]
    public string? Message { get; set; }
}
