using System.Globalization;
using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Contracts;
using KOSHI.Domain;
using KOSHI.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

public sealed class SettingsService(IKoshiDbContext db, AuditWriter audit, ICurrentUser currentUser, TimeProvider clock)
{
    public static class Keys
    {
        public const string TelemetryRetentionDays = "retention.telemetryDays";
        public const string HeartbeatRetentionDays = "retention.heartbeatDays";
        public const string AuditRetentionDays = "retention.auditDays";
        public const string OfflineThresholdSeconds = "devices.offlineThresholdSeconds";
        public const string OrganizationTimeZone = "organization.timeZone";
        public const string MonitoringNotice = "agent.monitoringNotice";
        public const string SupportContact = "agent.supportContact";
        public const string ApprovedUsageWeight = "productivity.approvedUsageWeight";
        public const string ScheduleAdherenceWeight = "productivity.scheduleAdherenceWeight";
        public const string ReportedActivityWeight = "productivity.reportedActivityWeight";
        public const string MinimumCoverage = "productivity.minimumCoverage";
    }

    public static readonly SystemSettingsDto Defaults = new(
        TelemetryRetentionDays: 90,
        HeartbeatRetentionDays: 30,
        AuditRetentionDays: 365,
        OfflineThresholdSeconds: 120,
        OrganizationTimeZone: "Asia/Tashkent",
        MonitoringNotice: "Bu korporativ kompyuter tashkilot siyosatiga muvofiq KOSHI orqali monitoring qilinadi: faol dastur nomi, faol/bo'sh vaqt va (siyosat ruxsat bersa) maskalangan oyna sarlavhasi. Klaviatura bosishlari, parollar, clipboard, kamera, mikrofon va shaxsiy xabarlar yig'ilmaydi.",
        SupportContact: "IT yordam: it-support@example.com",
        ApprovedUsageWeight: 0.5,
        ScheduleAdherenceWeight: 0.3,
        ReportedActivityWeight: 0.2,
        MinimumCoverage: 0.5);

    public async Task<SystemSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var map = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        var d = Defaults;
        return new SystemSettingsDto(
            Int(map, Keys.TelemetryRetentionDays, d.TelemetryRetentionDays),
            Int(map, Keys.HeartbeatRetentionDays, d.HeartbeatRetentionDays),
            Int(map, Keys.AuditRetentionDays, d.AuditRetentionDays),
            Int(map, Keys.OfflineThresholdSeconds, d.OfflineThresholdSeconds),
            Str(map, Keys.OrganizationTimeZone, d.OrganizationTimeZone),
            Str(map, Keys.MonitoringNotice, d.MonitoringNotice),
            Str(map, Keys.SupportContact, d.SupportContact),
            Dbl(map, Keys.ApprovedUsageWeight, d.ApprovedUsageWeight),
            Dbl(map, Keys.ScheduleAdherenceWeight, d.ScheduleAdherenceWeight),
            Dbl(map, Keys.ReportedActivityWeight, d.ReportedActivityWeight),
            Dbl(map, Keys.MinimumCoverage, d.MinimumCoverage));
    }

    public async Task<SystemSettingsDto> UpdateAsync(UpdateSystemSettingsRequest r, CancellationToken ct = default)
    {
        if (ScheduleCalculator.ResolveTimeZone(r.OrganizationTimeZone) == TimeZoneInfo.Utc &&
            !string.Equals(r.OrganizationTimeZone, "UTC", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(r.OrganizationTimeZone, "Etc/UTC", StringComparison.OrdinalIgnoreCase))
            throw Errors.Validation(nameof(r.OrganizationTimeZone), "Unknown time zone.");

        if (r.ApprovedUsageWeight + r.ScheduleAdherenceWeight + r.ReportedActivityWeight <= 0)
            throw Errors.Validation(nameof(r.ApprovedUsageWeight), "At least one productivity weight must be positive.");

        var values = new Dictionary<string, string>
        {
            [Keys.TelemetryRetentionDays] = r.TelemetryRetentionDays.ToString(CultureInfo.InvariantCulture),
            [Keys.HeartbeatRetentionDays] = r.HeartbeatRetentionDays.ToString(CultureInfo.InvariantCulture),
            [Keys.AuditRetentionDays] = r.AuditRetentionDays.ToString(CultureInfo.InvariantCulture),
            [Keys.OfflineThresholdSeconds] = r.OfflineThresholdSeconds.ToString(CultureInfo.InvariantCulture),
            [Keys.OrganizationTimeZone] = r.OrganizationTimeZone,
            [Keys.MonitoringNotice] = r.MonitoringNotice.Trim(),
            [Keys.SupportContact] = r.SupportContact.Trim(),
            [Keys.ApprovedUsageWeight] = r.ApprovedUsageWeight.ToString(CultureInfo.InvariantCulture),
            [Keys.ScheduleAdherenceWeight] = r.ScheduleAdherenceWeight.ToString(CultureInfo.InvariantCulture),
            [Keys.ReportedActivityWeight] = r.ReportedActivityWeight.ToString(CultureInfo.InvariantCulture),
            [Keys.MinimumCoverage] = r.MinimumCoverage.ToString(CultureInfo.InvariantCulture),
        };

        var existing = await db.SystemSettings.ToDictionaryAsync(s => s.Key, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = new List<string>();
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var s))
            {
                if (s.Value == value) continue;
                s.Value = value;
                s.UpdatedAt = now;
                s.UpdatedByUserId = currentUser.UserId;
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting { Id = Guid.NewGuid(), Key = key, Value = value, UpdatedAt = now, UpdatedByUserId = currentUser.UserId });
            }
            changed.Add(key);
        }

        audit.Record("settings.update", "SystemSettings", null, new { changed });
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    private static int Int(Dictionary<string, string> m, string k, int d) =>
        m.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : d;

    private static double Dbl(Dictionary<string, string> m, string k, double d) =>
        m.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : d;

    private static string Str(Dictionary<string, string> m, string k, string d) =>
        m.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : d;
}
