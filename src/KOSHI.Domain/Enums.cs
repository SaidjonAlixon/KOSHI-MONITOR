namespace KOSHI.Domain;

public enum DeviceStatus
{
    Pending = 0,
    Active = 1,
    Blocked = 2,
    Revoked = 3,
}

public enum ApplicationEventType
{
    Foreground = 0,
    Background = 1,
    PolicyViolation = 2,
}

public enum ActivityState
{
    Active = 0,
    Idle = 1,
    Paused = 2,
}

public enum PolicyStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

public enum PolicyMode
{
    AuditOnly = 0,
    Enforce = 1,
}

public enum WindowTitleCapture
{
    None = 0,
    Masked = 1,
    Full = 2,
}

public enum PolicyAssignmentScope
{
    AllDevices = 0,
    Department = 1,
    Device = 2,
}

public enum PolicyExecutionStatus
{
    Pending = 0,
    Applied = 1,
    Failed = 2,
    Unsupported = 3,
    RequiresAdministratorApproval = 4,
}

public enum ApplicationClassification
{
    Neutral = 0,
    Approved = 1,
    NotApproved = 2,
}

public enum ApplicationRestriction
{
    None = 0,
    Block = 1,
}

public enum AlertType
{
    DeviceOffline = 0,
    PolicyFailed = 1,
    PolicyViolation = 2,
    TelemetryGap = 3,
    Security = 4,
}

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

public enum AuditActorType
{
    User = 0,
    Device = 1,
    System = 2,
    Anonymous = 3,
}
