using System.Text.Json;
using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Application.Telemetry;
using KOSHI.Contracts;
using KOSHI.Contracts.Privacy;
using KOSHI.Domain;
using KOSHI.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KOSHI.Application.Services;

public sealed class TelemetryService(
    IKoshiDbContext db,
    PolicyService policies,
    SettingsService settings,
    IDbErrorClassifier dbErrors,
    TimeProvider clock,
    ILogger<TelemetryService> logger)
{
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public async Task<TelemetryBatchResponse> IngestAsync(Guid deviceId, TelemetryBatchRequest batch, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await IngestOnceAsync(deviceId, batch, ct);
            }
            catch (DbUpdateException ex) when (attempt < 3 && dbErrors.IsUniqueViolation(ex))
            {
                // A concurrent batch inserted the same idempotency key or summary row; re-run dedup.
                logger.LogInformation("Telemetry batch {BatchId} hit a unique constraint, retrying (attempt {Attempt})", batch.BatchId, attempt);
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<TelemetryBatchResponse> IngestOnceAsync(Guid deviceId, TelemetryBatchRequest batch, CancellationToken ct)
    {
        var device = await db.Devices.Include(d => d.Employee).FirstOrDefaultAsync(d => d.Id == deviceId, ct) ?? throw Errors.NotFound("Device");
        if (device.Status == DeviceStatus.Revoked || device.DeletedAt is not null || (device.LicenseExpiresAt is { } exp && exp <= clock.GetUtcNow().UtcDateTime))
            throw Errors.CredentialRevoked();
        if (device.Status != DeviceStatus.Active) throw Errors.DeviceBlocked();

        var now = clock.GetUtcNow().UtcDateTime;
        var s = await settings.GetAsync(ct);
        var tz = ScheduleCalculator.ResolveTimeZone(s.OrganizationTimeZone);
        var oldest = now.AddDays(-s.TelemetryRetentionDays);
        var effective = await policies.ResolveEffectiveAsync(device, ct);
        var capture = effective?.Snapshot.WindowTitleCapture ?? WindowTitleCaptureDto.None;
        var errors = new List<string>();
        var rejected = 0;

        bool ValidRange(string key, DateTime start, DateTime end)
        {
            string? why = null;
            if (end < start) why = "endedAt before startedAt";
            else if ((end - start).TotalSeconds > AgentLimits.MaxEventDurationSeconds) why = "duration exceeds 24h";
            else if (end > now + MaxClockSkew) why = "timestamp in the future";
            else if (start < oldest) why = "older than retention period";
            if (why is null) return true;
            rejected++;
            if (errors.Count < 20) errors.Add($"{key}: {why}");
            return false;
        }

        var events = new List<ApplicationEvent>();
        foreach (var e in batch.ApplicationEvents.DistinctBy(x => x.IdempotencyKey))
        {
            var start = ToUtc(e.StartedAt);
            var end = ToUtc(e.EndedAt);
            var processName = SanitizeProcessName(e.ProcessName);
            if (processName is null)
            {
                rejected++;
                if (errors.Count < 20) errors.Add($"{e.IdempotencyKey}: invalid process name");
                continue;
            }
            if (!ValidRange(e.IdempotencyKey, start, end)) continue;
            events.Add(new ApplicationEvent
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                EventType = (ApplicationEventType)(int)e.EventType,
                ProcessName = processName,
                WindowTitle = e.EventType == ApplicationEventTypeDto.Foreground ? WindowTitleMasker.Apply(e.WindowTitle, capture) : null,
                StartedAt = start,
                EndedAt = end,
                DurationSeconds = (int)Math.Round((end - start).TotalSeconds),
                ReceivedAt = now,
                IdempotencyKey = e.IdempotencyKey,
                Metadata = JsonSerializer.Serialize(new ApplicationEventMetadata(
                    SanitizeProcessName(e.ExecutableName), e.ProcessId, Truncate(batch.AgentVersion, 50))),
            });
        }

        var sessions = new List<ActivitySession>();
        foreach (var x in batch.ActivitySessions.DistinctBy(x => x.IdempotencyKey))
        {
            var start = ToUtc(x.StartedAt);
            var end = ToUtc(x.EndedAt);
            if (!ValidRange(x.IdempotencyKey, start, end)) continue;
            sessions.Add(new ActivitySession
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                State = (ActivityState)(int)x.State,
                StartedAt = start,
                EndedAt = end,
                DurationSeconds = (int)Math.Round((end - start).TotalSeconds),
                ReceivedAt = now,
                IdempotencyKey = x.IdempotencyKey,
            });
        }

        var duplicates = (batch.ApplicationEvents.Count - batch.ApplicationEvents.DistinctBy(x => x.IdempotencyKey).Count())
                         + (batch.ActivitySessions.Count - batch.ActivitySessions.DistinctBy(x => x.IdempotencyKey).Count());

        var eventKeys = events.Select(e => e.IdempotencyKey).ToList();
        var existingEventKeys = eventKeys.Count == 0 ? [] : await db.ApplicationEvents
            .Where(e => e.DeviceId == deviceId && eventKeys.Contains(e.IdempotencyKey)).Select(e => e.IdempotencyKey).ToHashSetAsync(ct);
        var sessionKeys = sessions.Select(e => e.IdempotencyKey).ToList();
        var existingSessionKeys = sessionKeys.Count == 0 ? [] : await db.ActivitySessions
            .Where(e => e.DeviceId == deviceId && sessionKeys.Contains(e.IdempotencyKey)).Select(e => e.IdempotencyKey).ToHashSetAsync(ct);

        var newEvents = events.Where(e => !existingEventKeys.Contains(e.IdempotencyKey)).ToList();
        var newSessions = sessions.Where(e => !existingSessionKeys.Contains(e.IdempotencyKey)).ToList();
        duplicates += events.Count - newEvents.Count + sessions.Count - newSessions.Count;

        db.ApplicationEvents.AddRange(newEvents);
        db.ActivitySessions.AddRange(newSessions);
        await ApplyUsageAsync(deviceId, UsageAggregator.Aggregate(newEvents, tz), now, ct);
        RaiseViolationAlerts(device, newEvents, tz, now);

        await db.SaveChangesAsync(ct);
        return new TelemetryBatchResponse(batch.BatchId, newEvents.Count + newSessions.Count, duplicates, rejected, errors);
    }

    private async Task ApplyUsageAsync(Guid deviceId, Dictionary<UsageKey, UsageDelta> deltas, DateTime now, CancellationToken ct)
    {
        if (deltas.Count == 0) return;
        var dates = deltas.Keys.Select(k => k.Date).Distinct().ToList();
        var names = deltas.Keys.Select(k => k.ProcessName).Distinct().ToList();
        var existing = await db.ApplicationUsageSummaries
            .Where(s => s.DeviceId == deviceId && dates.Contains(s.Date) && names.Contains(s.ProcessName))
            .ToDictionaryAsync(s => new UsageKey(s.Date, s.ProcessName), ct);

        foreach (var (key, d) in deltas)
        {
            if (!existing.TryGetValue(key, out var row))
            {
                row = new ApplicationUsageSummary { Id = Guid.NewGuid(), DeviceId = deviceId, Date = key.Date, ProcessName = key.ProcessName };
                db.ApplicationUsageSummaries.Add(row);
            }
            row.ForegroundSeconds += d.ForegroundSeconds;
            row.BackgroundSeconds += d.BackgroundSeconds;
            row.SessionCount += d.SessionCount;
            row.UpdatedAt = now;
        }
    }

    private void RaiseViolationAlerts(Device device, List<ApplicationEvent> events, TimeZoneInfo tz, DateTime now)
    {
        foreach (var v in events.Where(e => e.EventType == ApplicationEventType.PolicyViolation).DistinctBy(e => e.ProcessName))
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(v.StartedAt, tz));
            var key = $"violation:{device.Id}:{v.ProcessName}:{day:yyyyMMdd}";
            if (db.Alerts.Local.Any(a => a.DedupKey == key) || db.Alerts.Any(a => a.DedupKey == key)) continue;
            db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid(), DeviceId = device.Id, Type = AlertType.PolicyViolation, Severity = AlertSeverity.Warning,
                Message = $"Taqiqlangan dastur '{v.ProcessName}' {device.DisplayName} kompyuterida ishga tushirildi.", DedupKey = key, CreatedAt = now,
            });
        }
    }

    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>Keeps only the executable file name (no directories that could reveal user names), lower-cased.</summary>
    public static string? SanitizeProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();
        var slash = trimmed.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) trimmed = trimmed[(slash + 1)..];
        trimmed = new string(trimmed.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > AgentLimits.MaxProcessNameLength) trimmed = trimmed[..AgentLimits.MaxProcessNameLength];
        return trimmed.ToLowerInvariant();
    }

    private static string? Truncate(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
