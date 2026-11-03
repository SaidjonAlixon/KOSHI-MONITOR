using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Contracts;
using KOSHI.Domain;
using KOSHI.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

public sealed class MonitoringService(IKoshiDbContext db, SettingsService settings, ReportService reports, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxTimelineWindow = 20_000;

    public async Task<IReadOnlyList<LiveDeviceDto>> LiveAsync(MonitoringQuery q, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var threshold = TimeSpan.FromSeconds(s.OfflineThresholdSeconds);
        var devices = await ScopedDevices(q).Where(d => d.Status == DeviceStatus.Active || d.Status == DeviceStatus.Blocked)
            .OrderBy(d => d.DisplayName).ToListAsync(ct);

        return devices.Select(d =>
        {
            var online = d.IsOnline(now, threshold);
            var status = d.Status == DeviceStatus.Blocked ? "Blocked" : !online ? "Offline" : d.MonitoringEnabled ? "Online" : "Paused";
            var started = online ? d.CurrentActivityStartedAt : null;
            return new LiveDeviceDto(
                d.Id, d.DisplayName, d.Employee?.FullName, d.Employee?.Department?.Name,
                online ? d.CurrentProcessName : null,
                online ? d.CurrentWindowTitle : null,
                online ? d.CurrentState?.ToString() : null,
                started,
                started is { } st ? (int)Math.Max(0, (now - st).TotalSeconds) : null,
                d.LastHeartbeatAt, status, d.MonitoringEnabled && online);
        }).ToList();
    }

    public async Task<PagedResult<TimelineItemDto>> TimelineAsync(TimelineQuery q, CancellationToken ct = default)
    {
        if (q.Page * q.PageSize > MaxTimelineWindow) throw Errors.Validation("Page", "Requested page is too deep; narrow the date range or filters.");
        var (from, to) = Range(q);
        var devices = await ScopedDevices(q).Select(d => new { d.Id, d.DisplayName }).ToListAsync(ct);
        var ids = devices.Select(d => d.Id).ToList();
        var names = devices.ToDictionary(d => d.Id, d => d.DisplayName);
        var window = q.Page * q.PageSize;

        var apps = db.ApplicationEvents.AsNoTracking()
            .Where(e => ids.Contains(e.DeviceId) && e.EventType != ApplicationEventType.Background && e.EndedAt > from && e.StartedAt < to);
        var states = db.ActivitySessions.AsNoTracking()
            .Where(e => ids.Contains(e.DeviceId) && e.State != ActivityState.Active && e.EndedAt > from && e.StartedAt < to);

        var total = await apps.LongCountAsync(ct) + await states.LongCountAsync(ct);
        var appItems = await apps.OrderByDescending(e => e.StartedAt).Take(window)
            .Select(e => new TimelineItemDto(e.Id, e.DeviceId, "", e.EventType == ApplicationEventType.PolicyViolation ? "PolicyViolation" : "Application",
                e.ProcessName, e.WindowTitle, e.StartedAt, e.EndedAt, e.DurationSeconds)).ToListAsync(ct);
        var stateItems = await states.OrderByDescending(e => e.StartedAt).Take(window)
            .Select(e => new TimelineItemDto(e.Id, e.DeviceId, "", e.State == ActivityState.Idle ? "Idle" : "Paused",
                null, null, e.StartedAt, e.EndedAt, e.DurationSeconds)).ToListAsync(ct);

        var page = appItems.Concat(stateItems).OrderByDescending(i => i.StartedAt)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(i => i with { DeviceName = names.GetValueOrDefault(i.DeviceId, "") }).ToList();
        return new PagedResult<TimelineItemDto>(page, q.Page, q.PageSize, total);
    }

    public async Task<IReadOnlyList<ApplicationUsageDto>> ApplicationUsageAsync(MonitoringQuery q, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var tz = ScheduleCalculator.ResolveTimeZone(s.OrganizationTimeZone);
        var (from, to) = Range(q);
        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(from, tz));
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(to.AddTicks(-1), tz));
        var ids = await ScopedDevices(q).Select(d => d.Id).ToListAsync(ct);

        var rows = await db.ApplicationUsageSummaries.AsNoTracking()
            .Where(u => ids.Contains(u.DeviceId) && u.Date >= firstDate && u.Date <= lastDate)
            .GroupBy(u => u.ProcessName)
            .Select(g => new { ProcessName = g.Key, Fg = g.Sum(x => x.ForegroundSeconds), Bg = g.Sum(x => x.BackgroundSeconds), S = g.Sum(x => x.SessionCount) })
            .OrderByDescending(x => x.Fg).Take(200).ToListAsync(ct);
        var total = rows.Sum(r => (double)r.Fg);
        return rows.Select(r => new ApplicationUsageDto(r.ProcessName, r.Fg, r.Bg, r.S, total > 0 ? Math.Round(r.Fg / total, 4) : 0)).ToList();
    }

    public async Task<IdleSummaryDto> IdleSummaryAsync(MonitoringQuery q, CancellationToken ct = default)
    {
        var (from, to) = Range(q);
        var data = await reports.LoadAsync(from, to, q, null, ct);
        var t = data.Devices.Select(d => d.Time).Aggregate(new TimeBreakdown(), (a, b) => a + b);
        return new IdleSummaryDto(from, to, Math.Round(t.ActiveInScheduleSeconds), Math.Round(t.IdleInScheduleSeconds), Math.Round(t.PausedInScheduleSeconds),
            Math.Round(t.ScheduledBreakSeconds), Math.Round(t.OutsideScheduleSeconds), Math.Round(t.UnknownSeconds), Math.Round(t.ScheduledSeconds), data.Devices.Count);
    }

    private (DateTime From, DateTime To) Range(MonitoringQuery q)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var to = q.To is { } t ? TelemetryService.ToUtc(t) : now;
        var from = q.From is { } f ? TelemetryService.ToUtc(f) : to.AddDays(-1);
        if (to <= from) throw Errors.Validation("To", "'to' must be after 'from'.");
        if ((to - from).TotalDays > ReportService.MaxRangeDays) throw Errors.Validation("To", $"Date range must not exceed {ReportService.MaxRangeDays} days.");
        return (from, to);
    }

    private IQueryable<Device> ScopedDevices(MonitoringQuery q)
    {
        var departmentId = DataScope.EffectiveDepartment(currentUser, q.DepartmentId);
        var query = db.Devices.AsNoTracking().Include(d => d.Employee!).ThenInclude(e => e.Department).Where(d => d.DeletedAt == null);
        if (q.DeviceId is { } dev) query = query.Where(d => d.Id == dev);
        if (q.EmployeeId is { } emp) query = query.Where(d => d.EmployeeId == emp);
        if (departmentId is { } dep) query = query.Where(d => d.Employee != null && d.Employee.DepartmentId == dep);
        if (!string.IsNullOrWhiteSpace(q.Branch)) query = query.Where(d => d.Employee != null && d.Employee.Department != null && d.Employee.Department.Branch == q.Branch);
        return query;
    }
}
