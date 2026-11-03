using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Application.Security;
using KOSHI.Contracts;
using KOSHI.Contracts.Privacy;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KOSHI.Application.Services;

public enum DeviceAuthFailure { None, Invalid, Revoked }

public sealed record DeviceAuthResult(Guid DeviceId, Guid CredentialId, DeviceStatus Status, DeviceAuthFailure Failure)
{
    public static readonly DeviceAuthResult Invalid = new(Guid.Empty, Guid.Empty, DeviceStatus.Pending, DeviceAuthFailure.Invalid);
    public static readonly DeviceAuthResult Revoked = new(Guid.Empty, Guid.Empty, DeviceStatus.Revoked, DeviceAuthFailure.Revoked);
}

public sealed class DeviceService(
    IKoshiDbContext db,
    AuditWriter audit,
    SettingsService settings,
    PolicyService policies,
    ICurrentUser currentUser,
    IMonitoringNotifier notifier,
    TimeProvider clock,
    ILogger<DeviceService> logger)
{
    public async Task<ActivationCodeResponse> CreateAsync(CreateDeviceRequest r, CancellationToken ct = default)
    {
        await EnsureEmployeeAsync(r.EmployeeId, ct);
        var now = Now;
        var device = new Device
        {
            Id = Guid.NewGuid(),
            DisplayName = r.DisplayName.Trim(),
            EmployeeId = r.EmployeeId,
            Status = DeviceStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Devices.Add(device);
        var (code, activation) = NewActivation(device.Id, r.ActivationCodeTtlMinutes, now);
        db.DeviceActivations.Add(activation);
        audit.Record("device.create", "Device", device.Id, new { device.DisplayName, device.EmployeeId, activation.ExpiresAt });
        await db.SaveChangesAsync(ct);
        return new ActivationCodeResponse(device.Id, code, activation.ExpiresAt);
    }

    public async Task<ActivationCodeResponse> IssueActivationCodeAsync(Guid deviceId, int ttlMinutes, CancellationToken ct = default)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.DeletedAt == null, ct) ?? throw Errors.NotFound("Device");
        if (device.Status == DeviceStatus.Blocked) throw Errors.Conflict("Unblock the device before issuing a new activation code.");

        var now = Now;
        var open = await db.DeviceActivations.Where(a => a.DeviceId == deviceId && a.UsedAt == null && a.InvalidatedAt == null).ToListAsync(ct);
        foreach (var a in open) a.InvalidatedAt = now;

        var (code, activation) = NewActivation(deviceId, ttlMinutes, now);
        db.DeviceActivations.Add(activation);
        audit.Record("device.activation_code_issued", "Device", deviceId, new { activation.ExpiresAt, invalidatedPrevious = open.Count });
        await db.SaveChangesAsync(ct);
        return new ActivationCodeResponse(deviceId, code, activation.ExpiresAt);
    }

    public async Task<DeviceActivationResponse> ActivateAsync(DeviceActivationRequest r, CancellationToken ct = default)
    {
        var now = Now;
        var normalized = SecretUtil.NormalizeActivationCode(r.ActivationCode);
        var hash = SecretUtil.Sha256Hex(normalized);
        var activation = await db.DeviceActivations.Include(a => a.Device)
            .FirstOrDefaultAsync(a => a.CodeHash == hash, ct);

        if (activation?.Device is not { } device || !activation.IsUsable(now) ||
            device.Status is DeviceStatus.Blocked || device.DeletedAt is not null)
        {
            audit.Record("device.activate", "Device", activation?.DeviceId, new { reason = activation is null ? "unknown_code" : "unusable_code" }, success: false, actorType: AuditActorType.Anonymous);
            await db.SaveChangesAsync(ct);
            throw new AppException(400, ErrorCodes.ActivationInvalid, "Activation code is invalid, expired or already used.");
        }

        activation.UsedAt = now;
        activation.UsedFromIp = currentUser.IpAddress;

        var existing = await db.DeviceCredentials.Where(c => c.DeviceId == device.Id && c.RevokedAt == null).ToListAsync(ct);
        foreach (var c in existing) c.RevokedAt = now;

        var secret = SecretUtil.RandomToken();
        var credential = new DeviceCredential { Id = Guid.NewGuid(), DeviceId = device.Id, SecretHash = SecretUtil.Sha256Hex(secret), CreatedAt = now };
        db.DeviceCredentials.Add(credential);

        device.Status = DeviceStatus.Active;
        device.Hostname = r.Hostname.Trim();
        device.OsVersion = r.OsVersion?.Trim();
        device.AgentVersion = r.AgentVersion?.Trim();
        device.ActivatedAt = now;
        device.RevokedAt = null;
        device.UpdatedAt = now;

        audit.Record("device.activate", "Device", device.Id, new { device.Hostname, revokedPreviousCredentials = existing.Count }, deviceId: device.Id);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Device {DeviceId} activated", device.Id);
        return new DeviceActivationResponse(device.Id, SecretUtil.FormatDeviceCredential(credential.Id, secret), device.DisplayName);
    }

    public async Task<DeviceAuthResult> AuthenticateAsync(string? presented, CancellationToken ct = default)
    {
        if (!SecretUtil.TryParseDeviceCredential(presented, out var credentialId, out var secret)) return DeviceAuthResult.Invalid;

        var credential = await db.DeviceCredentials.Include(c => c.Device).FirstOrDefaultAsync(c => c.Id == credentialId, ct);
        if (credential?.Device is null) return DeviceAuthResult.Invalid;
        if (!SecretUtil.FixedTimeEqualsHex(credential.SecretHash, SecretUtil.Sha256Hex(secret))) return DeviceAuthResult.Invalid;
        if (credential.RevokedAt is not null || credential.Device.Status == DeviceStatus.Revoked || credential.Device.DeletedAt is not null)
            return DeviceAuthResult.Revoked;

        var now = Now;
        if (credential.LastUsedAt is null || now - credential.LastUsedAt > TimeSpan.FromMinutes(5))
        {
            credential.LastUsedAt = now;
            await db.SaveChangesAsync(ct);
        }
        return new DeviceAuthResult(credential.DeviceId, credential.Id, credential.Device.Status, DeviceAuthFailure.None);
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(Guid deviceId, HeartbeatRequest r, CancellationToken ct = default)
    {
        var now = Now;
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct) ?? throw Errors.NotFound("Device");
        if (device.Status == DeviceStatus.Revoked) throw Errors.CredentialRevoked();

        var allowed = device.Status == DeviceStatus.Active;
        var effective = await policies.ResolveEffectiveAsync(device, ct);
        var capture = effective?.Snapshot.WindowTitleCapture ?? WindowTitleCaptureDto.None;

        db.DeviceHeartbeats.Add(new DeviceHeartbeat
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            ReceivedAt = now,
            ClientTime = r.ClientTime is { } t ? TelemetryService.ToUtc(t) : null,
            AgentVersion = r.AgentVersion,
            MonitoringEnabled = r.MonitoringEnabled && allowed,
            QueueLength = r.QueueLength,
            PolicyVersion = r.PolicyVersion,
        });

        device.LastHeartbeatAt = now;
        device.AgentVersion = r.AgentVersion ?? device.AgentVersion;
        device.OsVersion = r.OsVersion ?? device.OsVersion;
        device.MonitoringEnabled = r.MonitoringEnabled && allowed;
        device.AppliedPolicyId = r.PolicyId;
        device.AppliedPolicyVersion = r.PolicyVersion;
        if (allowed && r.MonitoringEnabled)
        {
            device.CurrentProcessName = TelemetryService.SanitizeProcessName(r.CurrentProcessName);
            device.CurrentWindowTitle = WindowTitleMasker.Apply(r.CurrentWindowTitle, capture);
            device.CurrentState = r.CurrentState is { } s ? (ActivityState)(int)s : null;
            device.CurrentActivityStartedAt = r.CurrentActivityStartedAt is { } st ? TelemetryService.ToUtc(st) : null;
        }
        else
        {
            device.CurrentProcessName = null;
            device.CurrentWindowTitle = null;
            device.CurrentState = null;
            device.CurrentActivityStartedAt = null;
        }

        var openOffline = await db.Alerts.Where(a => a.DeviceId == deviceId && a.Type == AlertType.DeviceOffline && a.ResolvedAt == null).ToListAsync(ct);
        foreach (var a in openOffline) a.ResolvedAt = now;

        await db.SaveChangesAsync(ct);
        await notifier.DeviceChangedAsync(deviceId, ct);
        return new HeartbeatResponse(now, allowed, effective?.PolicyId, effective?.Version);
    }

    public async Task<PagedResult<DeviceDto>> ListAsync(DeviceQuery q, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var now = Now;
        var onlineSince = now.AddSeconds(-s.OfflineThresholdSeconds);
        var departmentId = DataScope.EffectiveDepartment(currentUser, q.DepartmentId);

        var query = db.Devices.AsNoTracking().Include(d => d.Employee!).ThenInclude(e => e.Department).Where(d => d.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToUpper();
            query = query.Where(d => d.DisplayName.ToUpper().Contains(term) || (d.Hostname != null && d.Hostname.ToUpper().Contains(term)) ||
                                     (d.Employee != null && d.Employee.FullName.ToUpper().Contains(term)));
        }
        if (q.Status is not null && Enum.TryParse<DeviceStatus>(q.Status, out var status)) query = query.Where(d => d.Status == status);
        if (q.Online is true) query = query.Where(d => d.Status == DeviceStatus.Active && d.LastHeartbeatAt >= onlineSince);
        if (q.Online is false) query = query.Where(d => !(d.Status == DeviceStatus.Active && d.LastHeartbeatAt >= onlineSince));
        if (departmentId is { } dep) query = query.Where(d => d.Employee != null && d.Employee.DepartmentId == dep);
        if (q.EmployeeId is { } emp) query = query.Where(d => d.EmployeeId == emp);

        query = (q.SortBy?.ToLowerInvariant(), q.SortDir) switch
        {
            ("lastheartbeatat", "asc") => query.OrderBy(d => d.LastHeartbeatAt),
            ("lastheartbeatat", _) => query.OrderByDescending(d => d.LastHeartbeatAt),
            ("status", "desc") => query.OrderByDescending(d => d.Status),
            ("status", _) => query.OrderBy(d => d.Status),
            ("createdat", "asc") => query.OrderBy(d => d.CreatedAt),
            ("createdat", _) => query.OrderByDescending(d => d.CreatedAt),
            (_, "desc") => query.OrderByDescending(d => d.DisplayName),
            _ => query.OrderBy(d => d.DisplayName),
        };

        var total = await query.LongCountAsync(ct);
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        var threshold = TimeSpan.FromSeconds(s.OfflineThresholdSeconds);
        return new PagedResult<DeviceDto>(items.Select(d => ToDto(d, now, threshold)).ToList(), q.Page, q.PageSize, total);
    }

    public async Task<DeviceDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var d = await LoadAsync(id, ct);
        return ToDto(d, Now, TimeSpan.FromSeconds(s.OfflineThresholdSeconds));
    }

    public async Task<DeviceDto> UpdateAsync(Guid id, UpdateDeviceRequest r, CancellationToken ct = default)
    {
        await EnsureEmployeeAsync(r.EmployeeId, ct);
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.DeletedAt == null, ct) ?? throw Errors.NotFound("Device");
        audit.Record("device.update", "Device", id, new { from = new { device.DisplayName, device.EmployeeId }, to = new { r.DisplayName, r.EmployeeId } });
        device.DisplayName = r.DisplayName.Trim();
        device.EmployeeId = r.EmployeeId;
        device.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<DeviceDto> BlockAsync(Guid id, string? reason, CancellationToken ct = default)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.DeletedAt == null, ct) ?? throw Errors.NotFound("Device");
        if (device.Status == DeviceStatus.Revoked) throw Errors.Conflict("Revoked devices cannot be blocked; re-enroll first.");
        device.Status = DeviceStatus.Blocked;
        device.BlockReason = reason?.Trim();
        device.BlockedAt = Now;
        device.UpdatedAt = Now;
        device.MonitoringEnabled = false;
        audit.Record("device.block", "Device", id, new { reason = device.BlockReason });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<DeviceDto> UnblockAsync(Guid id, CancellationToken ct = default)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.DeletedAt == null, ct) ?? throw Errors.NotFound("Device");
        if (device.Status != DeviceStatus.Blocked) throw Errors.Conflict("Device is not blocked.");
        var hasCredential = await db.DeviceCredentials.AnyAsync(c => c.DeviceId == id && c.RevokedAt == null, ct);
        device.Status = hasCredential ? DeviceStatus.Active : DeviceStatus.Pending;
        device.BlockReason = null;
        device.BlockedAt = null;
        device.UpdatedAt = Now;
        audit.Record("device.unblock", "Device", id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<DeviceDto> RevokeAsync(Guid id, CancellationToken ct = default)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.DeletedAt == null, ct) ?? throw Errors.NotFound("Device");
        var now = Now;
        var credentials = await db.DeviceCredentials.Where(c => c.DeviceId == id && c.RevokedAt == null).ToListAsync(ct);
        foreach (var c in credentials) c.RevokedAt = now;
        var codes = await db.DeviceActivations.Where(a => a.DeviceId == id && a.UsedAt == null && a.InvalidatedAt == null).ToListAsync(ct);
        foreach (var a in codes) a.InvalidatedAt = now;
        device.Status = DeviceStatus.Revoked;
        device.RevokedAt = now;
        device.UpdatedAt = now;
        device.MonitoringEnabled = false;
        device.CurrentProcessName = null;
        device.CurrentWindowTitle = null;
        device.CurrentState = null;
        audit.Record("device.revoke", "Device", id, new { revokedCredentials = credentials.Count, invalidatedCodes = codes.Count });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Records the start of a real-time agent connection (exact online time).</summary>
    public async Task MarkConnectedAsync(Guid deviceId, string connectionId, CancellationToken ct = default)
    {
        var now = Now;
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null) return;
        var stale = await db.DeviceConnections.Where(c => c.DeviceId == deviceId && c.DisconnectedAt == null && c.ConnectionId != connectionId).ToListAsync(ct);
        foreach (var s in stale) s.DisconnectedAt = now;
        db.DeviceConnections.Add(new DeviceConnection { Id = Guid.NewGuid(), DeviceId = deviceId, ConnectedAt = now, ConnectionId = connectionId });
        device.ConnectedAt ??= now;
        device.DisconnectedAt = null;
        var openOffline = await db.Alerts.Where(a => a.DeviceId == deviceId && a.Type == AlertType.DeviceOffline && a.ResolvedAt == null).ToListAsync(ct);
        foreach (var a in openOffline) a.ResolvedAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Records the end of a real-time agent connection (exact offline time).</summary>
    public async Task MarkDisconnectedAsync(Guid deviceId, string connectionId, CancellationToken ct = default)
    {
        var now = Now;
        var open = await db.DeviceConnections.Where(c => c.DeviceId == deviceId && c.ConnectionId == connectionId && c.DisconnectedAt == null).ToListAsync(ct);
        foreach (var c in open) c.DisconnectedAt = now;
        var stillOpen = await db.DeviceConnections.AnyAsync(c => c.DeviceId == deviceId && c.DisconnectedAt == null, ct);
        if (!stillOpen)
        {
            var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
            if (device is not null)
            {
                device.ConnectedAt = null;
                device.DisconnectedAt = now;
                device.CurrentProcessName = null;
                device.CurrentWindowTitle = null;
                device.CurrentState = null;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeviceConnectionDto>> ConnectionsAsync(Guid id, int days, CancellationToken ct = default)
    {
        await LoadAsync(id, ct);
        var since = Now.AddDays(-Math.Clamp(days, 1, 90));
        var rows = await db.DeviceConnections.AsNoTracking()
            .Where(c => c.DeviceId == id && (c.DisconnectedAt == null || c.DisconnectedAt >= since))
            .OrderByDescending(c => c.ConnectedAt).Take(500).ToListAsync(ct);
        return rows.Select(c => new DeviceConnectionDto(c.ConnectedAt, c.DisconnectedAt, c.DurationSeconds)).ToList();
    }

    public async Task<DeviceStatusDto> StatusAsync(Guid id, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var d = await LoadAsync(id, ct);
        var now = Now;
        var since = now.AddHours(-1);
        var recent = await db.DeviceHeartbeats.CountAsync(h => h.DeviceId == id && h.ReceivedAt >= since, ct);
        return new DeviceStatusDto(d.Id, d.Status.ToString(), d.IsOnline(now, TimeSpan.FromSeconds(s.OfflineThresholdSeconds)),
            d.LastHeartbeatAt, d.ConnectedAt, d.DisconnectedAt, d.MonitoringEnabled, d.CurrentProcessName, d.CurrentState?.ToString(), d.AppliedPolicyVersion, recent);
    }

    private async Task<Device> LoadAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Devices.AsNoTracking().Include(x => x.Employee!).ThenInclude(e => e.Department)
            .FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct) ?? throw Errors.NotFound("Device");
        DataScope.EffectiveDepartment(currentUser, d.Employee?.DepartmentId ?? currentUser.DepartmentScopeId);
        return d;
    }

    private async Task EnsureEmployeeAsync(Guid? employeeId, CancellationToken ct)
    {
        if (employeeId is { } e && !await db.Employees.AnyAsync(x => x.Id == e && x.DeletedAt == null, ct))
            throw Errors.Validation("EmployeeId", "Employee not found.");
    }

    private static (string Code, DeviceActivation Activation) NewActivation(Guid deviceId, int ttlMinutes, DateTime now)
    {
        var code = SecretUtil.NewActivationCode();
        return (code, new DeviceActivation
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            CodeHash = SecretUtil.Sha256Hex(SecretUtil.NormalizeActivationCode(code)),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(ttlMinutes),
        });
    }

    internal static DeviceDto ToDto(Device d, DateTime now, TimeSpan threshold) => new(
        d.Id, d.DisplayName, d.Hostname, d.OsVersion, d.AgentVersion, d.Status.ToString(), d.IsOnline(now, threshold),
        d.LastHeartbeatAt, d.EmployeeId, d.Employee?.FullName, d.Employee?.DepartmentId, d.Employee?.Department?.Name,
        d.MonitoringEnabled, d.AppliedPolicyId, d.AppliedPolicyVersion, d.CreatedAt, d.ActivatedAt, d.BlockReason);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
