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

    /// <summary>Set when the agent's real-time channel connects; null while the agent is offline. Gives an exact "online since".</summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>Time the agent channel last disconnected (exact "offline since").</summary>
    public DateTime? DisconnectedAt { get; set; }
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

/// <summary>One real-time connection session of an agent, giving an exact online/offline history.</summary>
public sealed class DeviceConnection
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public string? ConnectionId { get; set; }

    public int? DurationSeconds => DisconnectedAt is { } d ? (int)Math.Max(0, (d - ConnectedAt).TotalSeconds) : null;
}

/// <summary>
/// A position of a device. Consecutive reports at the same place extend <see cref="LastSeenAt"/> instead of
/// adding rows, so the table is a compact "where and from when until when" history.
/// </summary>
public sealed class DeviceLocation
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AccuracyMeters { get; set; }

    /// <summary>gps / wifi / cell / windows (precise, Windows Location service) or ip (approximate).</summary>
    public string Source { get; set; } = "windows";
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? Isp { get; set; }

    /// <summary>Public IP address the report came from, as seen by the server.</summary>
    public string? PublicIp { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
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
