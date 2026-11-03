namespace KOSHI.Domain;

public sealed class Device
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Hostname { get; set; }
    public string? OsVersion { get; set; }
    public string? AgentVersion { get; set; }
    public Guid? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Pending;
    public string? BlockReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? BlockedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public bool MonitoringEnabled { get; set; }

    // Snapshot of the current foreground activity, refreshed by heartbeats (used by Live Monitoring).
    public string? CurrentProcessName { get; set; }
    public string? CurrentWindowTitle { get; set; }
    public ActivityState? CurrentState { get; set; }
    public DateTime? CurrentActivityStartedAt { get; set; }
    public int? AppliedPolicyVersion { get; set; }
    public Guid? AppliedPolicyId { get; set; }
    public DateTime? DeletedAt { get; set; }

    public bool IsOnline(DateTime now, TimeSpan offlineThreshold) =>
        Status == DeviceStatus.Active && LastHeartbeatAt is { } hb && now - hb <= offlineThreshold;
}

public sealed class DeviceActivation
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>SHA-256 of the one-time code. The plaintext code is shown to the admin exactly once.</summary>
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? InvalidatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? UsedFromIp { get; set; }

    public bool IsUsable(DateTime now) => UsedAt is null && InvalidatedAt is null && ExpiresAt > now;
}

public sealed class DeviceCredential
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>SHA-256 of a 256-bit random secret.</summary>
    public string SecretHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public sealed class DeviceHeartbeat
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ClientTime { get; set; }
    public string? AgentVersion { get; set; }
    public bool MonitoringEnabled { get; set; }
    public int QueueLength { get; set; }
    public int? PolicyVersion { get; set; }
}
