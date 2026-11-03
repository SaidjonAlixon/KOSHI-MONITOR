namespace KOSHI.Domain;

public sealed class ApplicationEvent
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public ApplicationEventType EventType { get; set; }
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Only stored when the effective policy allows it (masked or full).</summary>
    public string? WindowTitle { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>JSONB built server-side from a fixed whitelist of fields (see ApplicationEventMetadata).</summary>
    public string? Metadata { get; set; }
}

/// <summary>The only fields allowed in ApplicationEvent.Metadata.</summary>
public sealed record ApplicationEventMetadata(string? ExecutableName, int? ProcessId, string? AgentVersion);

public sealed class ActivitySession
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public ActivityState State { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ApplicationUsageSummary
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>Calendar date in the organization time zone.</summary>
    public DateOnly Date { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public long ForegroundSeconds { get; set; }
    public long BackgroundSeconds { get; set; }
    public int SessionCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}
