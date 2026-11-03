using System.ComponentModel.DataAnnotations;

namespace KOSHI.Contracts;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);

public class PageQuery
{
    [Range(1, 100_000)]
    public int Page { get; set; } = 1;

    [Range(1, 200)]
    public int PageSize { get; set; } = 25;

    [StringLength(100)]
    public string? Search { get; set; }

    [StringLength(40)]
    [RegularExpression("^[A-Za-z]+$")]
    public string? SortBy { get; set; }

    [RegularExpression("^(asc|desc)$")]
    public string? SortDir { get; set; }
}

/// <summary>Stable machine-readable error codes returned in ProblemDetails.extensions.code.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountLocked = "account_locked";
    public const string InvalidToken = "invalid_token";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string Forbidden = "forbidden";
    public const string DeviceBlocked = "device_blocked";
    public const string CredentialRevoked = "credential_revoked";
    public const string ActivationInvalid = "activation_invalid";
    public const string PolicyInvalid = "policy_invalid";
    public const string RateLimited = "rate_limited";
    public const string InternalError = "internal_error";
}

public enum ActivityStateDto { Active = 0, Idle = 1, Paused = 2 }

public enum ApplicationEventTypeDto { Foreground = 0, Background = 1, PolicyViolation = 2 }

public enum PolicyModeDto { AuditOnly = 0, Enforce = 1 }

public enum WindowTitleCaptureDto { None = 0, Masked = 1, Full = 2 }

public enum ApplicationClassificationDto { Neutral = 0, Approved = 1, NotApproved = 2 }

public enum ApplicationRestrictionDto { None = 0, Block = 1 }

public enum PolicyAssignmentScopeDto { AllDevices = 0, Department = 1, Device = 2 }

public enum PolicyExecutionStatusDto
{
    Pending = 0,
    Applied = 1,
    Failed = 2,
    Unsupported = 3,
    RequiresAdministratorApproval = 4,
}
