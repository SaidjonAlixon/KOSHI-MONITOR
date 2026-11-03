using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Application.Reports;
using KOSHI.Contracts;
using KOSHI.Domain;
using KOSHI.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

public sealed class DashboardService(IKoshiDbContext db, SettingsService settings, ReportService reports, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<DashboardSummaryDto> SummaryAsync(MonitoringQuery q, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var tz = ScheduleCalculator.ResolveTimeZone(s.OrganizationTimeZone);
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, tz));
        var todayStart = ReportService.LocalMidnightUtc(today, tz);
        var weekStart = ReportService.LocalMidnightUtc(today.AddDays(-6), tz);
        var onlineSince = now.AddSeconds(-s.OfflineThresholdSeconds);
        var departmentId = DataScope.EffectiveDepartment(currentUser, q.DepartmentId);

        var devices = db.Devices.AsNoTracking().Where(d => d.DeletedAt == null);
        if (departmentId is { } dep) devices = devices.Where(d => d.Employee != null && d.Employee.DepartmentId == dep);
        if (!string.IsNullOrWhiteSpace(q.Branch)) devices = devices.Where(d => d.Employee != null && d.Employee.Department != null && d.Employee.Department.Branch == q.Branch);

        var counts = await devices.GroupBy(d => d.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(DeviceStatus st) => counts.Where(c => c.Key == st).Sum(c => c.Count);
        var total = counts.Sum(c => c.Count);
        var online = await devices.CountAsync(d => d.Status == DeviceStatus.Active && d.LastHeartbeatAt >= onlineSince, ct);
        var active = Count(DeviceStatus.Active);

        var ids = await devices.Select(d => d.Id).ToListAsync(ct);
        var activeApps = await db.ApplicationUsageSummaries.Where(u => ids.Contains(u.DeviceId) && u.Date == today && u.ForegroundSeconds > 0)
            .Select(u => u.ProcessName).Distinct().CountAsync(ct);
        var policyFailures = await db.PolicyExecutionResults
            .CountAsync(r => ids.Contains(r.DeviceId) && r.Status == PolicyExecutionStatus.Failed && r.PolicyVersion == r.Policy!.PublishedVersion, ct);
        var dayAgo = now.AddHours(-24);
        var withData = await db.ActivitySessions.Where(x => ids.Contains(x.DeviceId) && x.EndedAt >= dayAgo).Select(x => x.DeviceId).Distinct().CountAsync(ct);
        var openAlerts = await db.Alerts.CountAsync(a => a.ResolvedAt == null && a.AcknowledgedAt == null && (a.DeviceId == null || ids.Contains(a.DeviceId.Value)), ct);

        var scopedQuery = new MonitoringQuery { DepartmentId = departmentId, Branch = q.Branch };
        var todayData = await reports.LoadAsync(todayStart, now > todayStart ? now : todayStart.AddSeconds(1), scopedQuery, s, ct);
        var monitoredToday = todayData.Devices.Sum(d => d.Sessions.Sum(x => Overlap(x, todayStart, now)));

        var weekData = await reports.LoadAsync(weekStart, now > weekStart ? now : weekStart.AddSeconds(1), scopedQuery, s, ct);
        var daily = ReportService.Daily(weekData);
        var weights = ReportService.Weights(s);
        var departments = ReportService.Group(weekData, "department", weights)
            .Select(r => new DepartmentKpiDto(Guid.TryParse(r.GroupKey, out var g) ? g : null, r.GroupName, r.IsReliable ? r.ProductivityScore : null, r.Coverage, r.ActiveSeconds, r.IdleSeconds))
            .ToList();
        var apps = weekData.Devices.SelectMany(d => d.Apps).GroupBy(a => a.Key)
            .Select(g => new { Name = g.Key, Fg = g.Sum(x => x.Value.Foreground), Bg = g.Sum(x => x.Value.Background), S = g.Sum(x => x.Value.Sessions) })
            .OrderByDescending(x => x.Fg).Take(8).ToList();
        var appTotal = apps.Sum(a => (double)a.Fg);

        return new DashboardSummaryDto(
            total, online, Math.Max(0, active - online), Count(DeviceStatus.Pending), Count(DeviceStatus.Blocked),
            Math.Round(monitoredToday), activeApps, policyFailures, Math.Max(0, active - withData), openAlerts, daily,
            apps.Select(a => new ApplicationUsageDto(a.Name, a.Fg, a.Bg, a.S, appTotal > 0 ? Math.Round(a.Fg / appTotal, 4) : 0)).ToList(),
            departments, now);
    }

    private static double Overlap(StateInterval x, DateTime from, DateTime to)
    {
        var s = x.Start > from ? x.Start : from;
        var e = x.End < to ? x.End : to;
        return e > s ? (e - s).TotalSeconds : 0;
    }
}
