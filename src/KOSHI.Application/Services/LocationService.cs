using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Contracts;
using KOSHI.Domain;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

/// <summary>Device positions reported by agents (Windows Location: GPS / Wi-Fi, or approximate IP location).</summary>
public sealed class LocationService(IKoshiDbContext db, SettingsService settings, ICurrentUser currentUser, IMonitoringNotifier notifier, TimeProvider clock)
{
    // Reports closer than this to the previous point (or inside its accuracy radius) extend that point instead of adding a new one.
    private const double SamePlaceMeters = 75;
    private static readonly TimeSpan MaxExtendGap = TimeSpan.FromHours(2);

    public async Task ReportAsync(Guid deviceId, DeviceLocationReportRequest r, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        if (!s.AgentLocationEnabled) return;
        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deviceId && d.DeletedAt == null, ct) ?? throw Errors.NotFound("Device");
        if (device.Status != DeviceStatus.Active) return;

        var now = clock.GetUtcNow().UtcDateTime;
        var captured = r.CapturedAt is { } c ? TelemetryService.ToUtc(c) : now;
        if (captured > now.AddMinutes(5) || captured < now.AddDays(-1)) captured = now;

        var last = await db.DeviceLocations.Where(l => l.DeviceId == deviceId).OrderByDescending(l => l.LastSeenAt).FirstOrDefaultAsync(ct);
        if (last is not null && captured - last.LastSeenAt <= MaxExtendGap && IsSamePlace(last, r))
        {
            if (captured > last.LastSeenAt) last.LastSeenAt = captured;
            if (r.AccuracyMeters is { } acc && (last.AccuracyMeters is null || acc < last.AccuracyMeters))
            {
                last.Latitude = r.Latitude;
                last.Longitude = r.Longitude;
                last.AccuracyMeters = acc;
                last.Source = r.Source;
            }
            last.PublicIp = currentUser.IpAddress ?? last.PublicIp;
        }
        else
        {
            db.DeviceLocations.Add(new DeviceLocation
            {
                Id = Guid.NewGuid(), DeviceId = deviceId,
                Latitude = r.Latitude, Longitude = r.Longitude, AccuracyMeters = r.AccuracyMeters, Source = r.Source,
                City = Clean(r.City), Region = Clean(r.Region), Country = Clean(r.Country), Isp = Clean(r.Isp),
                PublicIp = currentUser.IpAddress, FirstSeenAt = captured, LastSeenAt = captured,
            });
        }
        await db.SaveChangesAsync(ct);
        await notifier.DeviceChangedAsync(deviceId, ct);
    }

    /// <summary>Latest known position of every device visible to the current admin.</summary>
    public async Task<IReadOnlyList<DeviceLocationDto>> LatestAsync(Guid? departmentId, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var dep = DataScope.EffectiveDepartment(currentUser, departmentId);
        var devicesQuery = db.Devices.AsNoTracking().Include(d => d.Employee!).ThenInclude(e => e.Department).Where(d => d.DeletedAt == null);
        if (dep is { } depId) devicesQuery = devicesQuery.Where(d => d.Employee != null && d.Employee.DepartmentId == depId);
        var devices = await devicesQuery.ToDictionaryAsync(d => d.Id, ct);
        var ids = devices.Keys.ToList();

        var latest = await db.DeviceLocations.AsNoTracking()
            .Where(l => ids.Contains(l.DeviceId))
            .GroupBy(l => l.DeviceId)
            .Select(g => g.OrderByDescending(x => x.LastSeenAt).First())
            .ToListAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var threshold = TimeSpan.FromSeconds(s.OfflineThresholdSeconds);
        return latest.OrderByDescending(l => l.LastSeenAt).Select(l =>
        {
            var d = devices[l.DeviceId];
            return new DeviceLocationDto(d.Id, d.DisplayName, d.Hostname, d.Employee?.FullName, d.Employee?.Department?.Name, d.IsOnline(now, threshold),
                l.Latitude, l.Longitude, l.AccuracyMeters, l.Source, l.City, l.Region, l.Country, l.Isp, l.PublicIp, l.FirstSeenAt, l.LastSeenAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<LocationPointDto>> HistoryAsync(Guid deviceId, int days, CancellationToken ct = default)
    {
        var device = await db.Devices.AsNoTracking().Include(d => d.Employee).FirstOrDefaultAsync(d => d.Id == deviceId && d.DeletedAt == null, ct)
                     ?? throw Errors.NotFound("Device");
        DataScope.EffectiveDepartment(currentUser, device.Employee?.DepartmentId ?? currentUser.DepartmentScopeId);
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-Math.Clamp(days, 1, 90));
        var rows = await db.DeviceLocations.AsNoTracking()
            .Where(l => l.DeviceId == deviceId && l.LastSeenAt >= since)
            .OrderBy(l => l.FirstSeenAt).Take(2000).ToListAsync(ct);
        return rows.Select(l => new LocationPointDto(l.Latitude, l.Longitude, l.AccuracyMeters, l.Source, l.City, l.Country, l.FirstSeenAt, l.LastSeenAt)).ToList();
    }

    private static bool IsSamePlace(DeviceLocation last, DeviceLocationReportRequest r)
    {
        var radius = Math.Max(SamePlaceMeters, Math.Max(last.AccuracyMeters ?? 0, r.AccuracyMeters ?? 0));
        return DistanceMeters(last.Latitude, last.Longitude, r.Latitude, r.Longitude) <= radius;
    }

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
