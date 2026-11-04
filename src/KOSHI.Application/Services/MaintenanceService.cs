using KOSHI.Application.Abstractions;
using KOSHI.Contracts;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KOSHI.Application.Services;

public sealed record RetentionCutoffs(DateTime Telemetry, DateTime Heartbeats, DateTime Audit, DateTime Tokens, DateTime Activations);

public sealed record RetentionResult(int ApplicationEvents, int ActivitySessions, int UsageSummaries, int Heartbeats, int AuditLogs, int RefreshTokens, int Activations, int Locations);

public sealed class MaintenanceService(IKoshiDbContext db, SettingsService settings, TimeProvider clock, ILogger<MaintenanceService> logger)
{
    public static RetentionCutoffs ComputeCutoffs(SystemSettingsDto s, DateTime now) => new(
        Telemetry: now.AddDays(-s.TelemetryRetentionDays),
        Heartbeats: now.AddDays(-s.HeartbeatRetentionDays),
        Audit: now.AddDays(-s.AuditRetentionDays),
        Tokens: now.AddDays(-30),
        Activations: now.AddDays(-30));

    /// <summary>Deletes expired event data in bounded statements. Audit logs are kept for their own (longer) retention.</summary>
    public async Task<RetentionResult> ApplyRetentionAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var c = ComputeCutoffs(s, clock.GetUtcNow().UtcDateTime);
        var cutoffDate = DateOnly.FromDateTime(c.Telemetry);

        var result = new RetentionResult(
            await db.ApplicationEvents.Where(e => e.EndedAt < c.Telemetry).ExecuteDeleteAsync(ct),
            await db.ActivitySessions.Where(e => e.EndedAt < c.Telemetry).ExecuteDeleteAsync(ct),
            await db.ApplicationUsageSummaries.Where(e => e.Date < cutoffDate).ExecuteDeleteAsync(ct),
            await db.DeviceHeartbeats.Where(e => e.ReceivedAt < c.Heartbeats).ExecuteDeleteAsync(ct),
            await db.AuditLogs.Where(e => e.OccurredAt < c.Audit).ExecuteDeleteAsync(ct),
            await db.RefreshTokens.Where(t => t.ExpiresAt < c.Tokens).ExecuteDeleteAsync(ct),
            await db.DeviceActivations.Where(a => a.ExpiresAt < c.Activations && a.UsedAt == null).ExecuteDeleteAsync(ct),
            await db.DeviceLocations.Where(l => l.LastSeenAt < c.Telemetry).ExecuteDeleteAsync(ct));

        logger.LogInformation("Retention applied: {@Result}", result);
        return result;
    }

    /// <summary>Opens one DeviceOffline alert per device that stopped sending heartbeats; heartbeats resolve it.</summary>
    public async Task<int> DetectOfflineDevicesAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var threshold = now.AddSeconds(-Math.Max(s.OfflineThresholdSeconds * 5, 600));
        var stale = await db.Devices.Where(d => d.Status == DeviceStatus.Active && d.DeletedAt == null && d.LastHeartbeatAt != null && d.LastHeartbeatAt < threshold)
            .Select(d => new { d.Id, d.DisplayName, d.LastHeartbeatAt }).ToListAsync(ct);
        var created = 0;
        foreach (var d in stale)
        {
            var key = $"offline:{d.Id}";
            if (await db.Alerts.AnyAsync(a => a.DedupKey == key && a.ResolvedAt == null, ct)) continue;
            db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid(), DeviceId = d.Id, Type = AlertType.DeviceOffline, Severity = AlertSeverity.Info, DedupKey = key, CreatedAt = now,
                Message = $"{d.DisplayName} has not sent a heartbeat since {d.LastHeartbeatAt:yyyy-MM-dd HH:mm} UTC.",
            });
            created++;
        }
        if (created > 0) await db.SaveChangesAsync(ct);
        return created;
    }
}
