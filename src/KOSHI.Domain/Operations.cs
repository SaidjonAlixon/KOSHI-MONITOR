namespace KOSHI.Domain;

public sealed class Alert
{
    public Guid Id { get; set; }
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Prevents duplicate open alerts for the same condition (e.g. "offline:{deviceId}").</summary>
    public string? DedupKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public sealed class AuditLog
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public AuditActorType ActorType { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorEmail { get; set; }
    public Guid? ActorDeviceId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public bool Success { get; set; } = true;
    public string? IpAddress { get; set; }

    /// <summary>Small JSONB payload; never contains secrets, codes, tokens or passwords.</summary>
    public string? Details { get; set; }
}

public sealed class SystemSetting
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
