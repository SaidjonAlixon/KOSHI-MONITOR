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
        public const string AgentAutoUpdate = "agent.autoUpdate";
        public const string AgentProtectionEnabled = "agent.protectionEnabled";
        public const string AgentProtectionSalt = "agent.protectionSalt";
        public const string AgentProtectionHash = "agent.protectionHash";
        public const string AgentLocationEnabled = "agent.locationEnabled";
        public const string AgentVpnBlocked = "agent.vpnBlocked";
        public const string AgentInstallBlocked = "agent.installBlocked";
    }

    public static readonly SystemSettingsDto Defaults = new(
        TelemetryRetentionDays: 90,
        HeartbeatRetentionDays: 30,
        AuditRetentionDays: 365,
        OfflineThresholdSeconds: 120,
        OrganizationTimeZone: "Asia/Tashkent",
        MonitoringNotice: "Korporativ kompyuter monitoringi faol.",
        SupportContact: "",
        ApprovedUsageWeight: 0.5,
        ScheduleAdherenceWeight: 0.3,
        ReportedActivityWeight: 0.2,
        MinimumCoverage: 0.5,
        AgentAutoUpdate: true,
        AgentProtectionEnabled: false,
        AgentLocationEnabled: true);

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
            Dbl(map, Keys.MinimumCoverage, d.MinimumCoverage),
            Bool(map, Keys.AgentAutoUpdate, d.AgentAutoUpdate),
            Bool(map, Keys.AgentProtectionEnabled, d.AgentProtectionEnabled),
            Bool(map, Keys.AgentLocationEnabled, d.AgentLocationEnabled),
            AgentProtectionPasswordSet: !string.IsNullOrEmpty(map.GetValueOrDefault(Keys.AgentProtectionHash)));
    }

    /// <summary>Returns the Windows-Settings protection state and password verifier for agents.</summary>
    public async Task<(bool Enabled, string? Salt, string? Hash)> GetProtectionAsync(CancellationToken ct = default)
    {
        var map = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        var enabled = Bool(map, Keys.AgentProtectionEnabled, false);
        var salt = map.GetValueOrDefault(Keys.AgentProtectionSalt);
        var hash = map.GetValueOrDefault(Keys.AgentProtectionHash);
        return (enabled && !string.IsNullOrEmpty(hash), salt, hash);
    }

    public async Task<DeviceControlsDto> GetControlsAsync(CancellationToken ct = default)
    {
        var map = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        return new DeviceControlsDto(
            Bool(map, Keys.AgentVpnBlocked, false),
            Bool(map, Keys.AgentInstallBlocked, false),
            !string.IsNullOrEmpty(map.GetValueOrDefault(Keys.AgentProtectionHash)));
    }

    public async Task<DeviceControlsDto> UpdateControlsAsync(UpdateDeviceControlsRequest r, CancellationToken ct = default)
    {
        var current = await GetControlsAsync(ct);
        if (r.InstallBlocked && !current.ProtectionPasswordSet)
            throw Errors.Validation(nameof(r.InstallBlocked), "Avval Sozlamalar → Tizim himoyasi bo‘limida himoya parolini o‘rnating: o‘rnatish shu parol bilan ochiladi.");

        var values = new Dictionary<string, string>
        {
            [Keys.AgentVpnBlocked] = r.VpnBlocked ? "true" : "false",
            [Keys.AgentInstallBlocked] = r.InstallBlocked ? "true" : "false",
        };
        var keys = values.Keys.ToList();
        var existing = await db.SystemSettings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);
        var now = clock.GetUtcNow().UtcDateTime;
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
        }
        audit.Record("controls.update", "SystemSettings", null, new { r.VpnBlocked, r.InstallBlocked });
        await db.SaveChangesAsync(ct);
        return await GetControlsAsync(ct);
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
            [Keys.AgentAutoUpdate] = r.AgentAutoUpdate ? "true" : "false",
            [Keys.AgentProtectionEnabled] = r.AgentProtectionEnabled ? "true" : "false",
            [Keys.AgentLocationEnabled] = r.AgentLocationEnabled ? "true" : "false",
        };

        var existingProtHash = await db.SystemSettings.AsNoTracking().Where(s => s.Key == Keys.AgentProtectionHash).Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (r.AgentProtectionEnabled)
        {
            if (!string.IsNullOrEmpty(r.AgentProtectionPassword))
            {
                if (!IsValidProtectionPassword(r.AgentProtectionPassword))
                    throw Errors.Validation(nameof(r.AgentProtectionPassword), "Parol aniq 7 ta belgidan iborat bo‘lishi kerak (bo‘sh joysiz).");
                var (salt, hash) = AppLockPassword.Create(r.AgentProtectionPassword);
                values[Keys.AgentProtectionSalt] = salt;
                values[Keys.AgentProtectionHash] = hash;
            }
            else if (string.IsNullOrEmpty(existingProtHash))
            {
                throw Errors.Validation(nameof(r.AgentProtectionPassword), "Himoyani yoqish uchun parol kiriting.");
            }
        }

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

    private static bool Bool(Dictionary<string, string> m, string k, bool d) =>
        m.TryGetValue(k, out var v) ? v == "true" : d;

    /// <summary>Protection password must be exactly 7 characters (any combination, no spaces).</summary>
    public static bool IsValidProtectionPassword(string p) =>
        !string.IsNullOrEmpty(p) && p.Length == 7 && !p.Any(char.IsWhiteSpace);

    private static string Str(Dictionary<string, string> m, string k, string d) =>
        m.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : d;
}
