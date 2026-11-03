using KOSHI.Application.Abstractions;
using KOSHI.Application.Common;
using KOSHI.Application.Policies;
using KOSHI.Application.Reports;
using KOSHI.Application.Security;
using KOSHI.Contracts;
using KOSHI.Domain;
using KOSHI.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace KOSHI.Application.Services;

public sealed record DeviceReportData(
    Device Device,
    WorkSchedule Schedule,
    TimeBreakdown Time,
    IReadOnlyList<StateInterval> Sessions,
    IReadOnlyDictionary<string, (long Foreground, long Background, int Sessions)> Apps,
    IReadOnlyDictionary<string, ApplicationClassificationDto> Classification);

public sealed record ReportData(DateTime From, DateTime To, TimeZoneInfo TimeZone, SystemSettingsDto Settings, IReadOnlyList<DeviceReportData> Devices);

public sealed class ReportService(
    IKoshiDbContext db,
    PolicyService policies,
    SettingsService settings,
    ICurrentUser currentUser,
    AuditWriter audit,
    TimeProvider clock)
{
    public const int MaxRangeDays = 93;

    public Task<ReportDto> DailyAsync(ReportQuery q, CancellationToken ct = default) => PeriodAsync("daily", q, ct);
    public Task<ReportDto> WeeklyAsync(ReportQuery q, CancellationToken ct = default) => PeriodAsync("weekly", q, ct);
    public Task<ReportDto> MonthlyAsync(ReportQuery q, CancellationToken ct = default) => PeriodAsync("monthly", q, ct);

    public async Task<ReportDto> ProductivityAsync(ReportQuery q, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var to = q.To is { } t ? TelemetryService.ToUtc(t) : now;
        var from = q.From is { } f ? TelemetryService.ToUtc(f) : to.AddDays(-7);
        return await BuildAsync("productivity", from, to, q, s, ct);
    }

    public async Task<(string FileName, string Content)> ExportCsvAsync(string type, ReportQuery q, CancellationToken ct = default)
    {
        var report = type switch
        {
            "daily" => await DailyAsync(q, ct),
            "weekly" => await WeeklyAsync(q, ct),
            "monthly" => await MonthlyAsync(q, ct),
            "productivity" => await ProductivityAsync(q, ct),
            _ => throw Errors.Validation("type", "type must be one of daily, weekly, monthly, productivity."),
        };
        audit.Record("report.export", "Report", type, new { report.From, report.To, report.GroupBy, rows = report.Rows.Count, q.DepartmentId, q.DeviceId, q.EmployeeId });
        await db.SaveChangesAsync(ct);
        return ($"koshi-{type}-{report.From:yyyyMMdd}-{report.To:yyyyMMdd}.csv", CsvExporter.Write(report));
    }

    public static (DateTime From, DateTime To) PeriodRange(string period, DateOnly anchor, TimeZoneInfo tz)
    {
        DateOnly start, end;
        switch (period)
        {
            case "weekly":
                var offset = ((int)anchor.DayOfWeek + 6) % 7;
                start = anchor.AddDays(-offset);
                end = start.AddDays(7);
                break;
            case "monthly":
                start = new DateOnly(anchor.Year, anchor.Month, 1);
                end = start.AddMonths(1);
                break;
            default:
                start = anchor;
                end = anchor.AddDays(1);
                break;
        }
        return (LocalMidnightUtc(start, tz), LocalMidnightUtc(end, tz));
    }

    public static DateTime LocalMidnightUtc(DateOnly date, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), tz);

    private async Task<ReportDto> PeriodAsync(string period, ReportQuery q, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var tz = ScheduleCalculator.ResolveTimeZone(s.OrganizationTimeZone);
        var anchor = q.Date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, tz));
        var (from, to) = PeriodRange(period, anchor, tz);
        return await BuildAsync(period, from, to, q, s, ct);
    }

    private async Task<ReportDto> BuildAsync(string period, DateTime from, DateTime to, ReportQuery q, SystemSettingsDto s, CancellationToken ct)
    {
        var groupBy = q.GroupBy ?? "employee";
        if (!currentUser.HasPermission(Permissions.ReportsIndividual)) groupBy = "department";

        var data = await LoadAsync(from, to, q, s, ct);
        var weights = Weights(s);
        var rows = Group(data, groupBy, weights);
        var daily = Daily(data);

        return new ReportDto(period, from, to, data.TimeZone.Id, groupBy,
            new ProductivityFormulaDto(weights.ApprovedUsage, weights.ScheduleAdherence, weights.ReportedActivity, weights.MinimumCoverage, ProductivityCalculator.Description),
            rows, daily, ProductivityCalculator.Disclaimer);
    }

    public static ProductivityWeights Weights(SystemSettingsDto s) =>
        new(s.ApprovedUsageWeight, s.ScheduleAdherenceWeight, s.ReportedActivityWeight, s.MinimumCoverage);

    /// <summary>Loads devices in scope with their schedules, classified time and per-app usage for [from, to).</summary>
    public async Task<ReportData> LoadAsync(DateTime from, DateTime to, MonitoringQuery q, SystemSettingsDto? s = null, CancellationToken ct = default)
    {
        if (to <= from) throw Errors.Validation("To", "'to' must be after 'from'.");
        if ((to - from).TotalDays > MaxRangeDays) throw Errors.Validation("To", $"Date range must not exceed {MaxRangeDays} days.");

        s ??= await settings.GetAsync(ct);
        var tz = ScheduleCalculator.ResolveTimeZone(s.OrganizationTimeZone);
        var departmentId = DataScope.EffectiveDepartment(currentUser, q.DepartmentId);

        var deviceQuery = db.Devices.AsNoTracking()
            .Include(d => d.Employee!).ThenInclude(e => e.Department)
            .Include(d => d.Employee!).ThenInclude(e => e.WorkSchedule)
            .Where(d => d.DeletedAt == null && d.Status != DeviceStatus.Pending);
        if (q.DeviceId is { } dev) deviceQuery = deviceQuery.Where(d => d.Id == dev);
        if (q.EmployeeId is { } emp) deviceQuery = deviceQuery.Where(d => d.EmployeeId == emp);
        if (departmentId is { } dep) deviceQuery = deviceQuery.Where(d => d.Employee != null && d.Employee.DepartmentId == dep);
        if (!string.IsNullOrWhiteSpace(q.Branch)) deviceQuery = deviceQuery.Where(d => d.Employee != null && d.Employee.Department != null && d.Employee.Department.Branch == q.Branch);
        var devices = await deviceQuery.ToListAsync(ct);
        var ids = devices.Select(d => d.Id).ToList();

        var sessions = await db.ActivitySessions.AsNoTracking()
            .Where(x => ids.Contains(x.DeviceId) && x.EndedAt > from && x.StartedAt < to)
            .Select(x => new { x.DeviceId, x.State, x.StartedAt, x.EndedAt })
            .ToListAsync(ct);
        var sessionsByDevice = sessions.GroupBy(x => x.DeviceId)
            .ToDictionary(g => g.Key, g => g.Select(x => new StateInterval(x.State, x.StartedAt, x.EndedAt)).ToList());

        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(from, tz));
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(to.AddTicks(-1), tz));
        var usage = await db.ApplicationUsageSummaries.AsNoTracking()
            .Where(u => ids.Contains(u.DeviceId) && u.Date >= firstDate && u.Date <= lastDate)
            .Select(u => new { u.DeviceId, u.ProcessName, u.ForegroundSeconds, u.BackgroundSeconds, u.SessionCount })
            .ToListAsync(ct);
        var usageByDevice = usage.GroupBy(u => u.DeviceId).ToDictionary(g => g.Key, g => g
            .GroupBy(x => x.ProcessName)
            .ToDictionary(x => x.Key, x => (x.Sum(y => y.ForegroundSeconds), x.Sum(y => y.BackgroundSeconds), x.Sum(y => y.SessionCount))));

        var published = await policies.LoadPublishedAsync(ct);
        var defaultSchedule = await DefaultScheduleAsync(s, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        var result = new List<DeviceReportData>();
        foreach (var d in devices)
        {
            var effective = PolicyResolver.Resolve(new DeviceTarget(d.Id, d.Employee?.DepartmentId), published, now);
            var schedule = d.Employee?.WorkSchedule
                           ?? (effective?.Snapshot.WorkSchedule is { } ws ? WorkScheduleMapping.ToEntity(ws) : null)
                           ?? defaultSchedule;
            var deviceSessions = sessionsByDevice.GetValueOrDefault(d.Id) ?? [];
            var breakdown = ScheduleCalculator.Classify(deviceSessions, schedule, from, to);
            var classification = (effective?.Snapshot.Rules ?? [])
                .GroupBy(r => r.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Classification, StringComparer.OrdinalIgnoreCase);
            result.Add(new DeviceReportData(d, schedule, breakdown, deviceSessions,
                usageByDevice.GetValueOrDefault(d.Id) ?? new Dictionary<string, (long, long, int)>(), classification));
        }

        return new ReportData(from, to, tz, s, result);
    }

    public static List<ReportRowDto> Group(ReportData data, string groupBy, ProductivityWeights weights)
    {
        var groups = data.Devices.GroupBy(d => groupBy switch
        {
            "device" => (Key: d.Device.Id.ToString(), Name: d.Device.DisplayName),
            "department" => (Key: d.Device.Employee?.DepartmentId?.ToString() ?? "none", Name: d.Device.Employee?.Department?.Name ?? "Bo'limsiz"),
            _ => (Key: d.Device.EmployeeId?.ToString() ?? $"device:{d.Device.Id}", Name: d.Device.Employee?.FullName ?? $"{d.Device.DisplayName} (biriktirilmagan)"),
        });

        var rows = new List<ReportRowDto>();
        foreach (var g in groups)
        {
            var time = g.Select(x => x.Time).Aggregate(new TimeBreakdown(), (a, b) => a + b);
            double approved = 0, notApproved = 0;
            var apps = new Dictionary<string, (long Fg, long Bg, int S)>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in g)
            {
                foreach (var (name, u) in d.Apps)
                {
                    var c = d.Classification.GetValueOrDefault(name, ApplicationClassificationDto.Neutral);
                    if (c == ApplicationClassificationDto.Approved) approved += u.Foreground;
                    if (c == ApplicationClassificationDto.NotApproved) notApproved += u.Foreground;
                    apps[name] = apps.TryGetValue(name, out var a) ? (a.Fg + u.Foreground, a.Bg + u.Background, a.S + u.Sessions) : (u.Foreground, u.Background, u.Sessions);
                }
            }

            var totalFg = apps.Values.Sum(a => (double)a.Fg);
            var top = apps.OrderByDescending(a => a.Value.Fg).Take(5)
                .Select(a => new ApplicationUsageDto(a.Key, a.Value.Fg, a.Value.Bg, a.Value.S, totalFg > 0 ? Math.Round(a.Value.Fg / totalFg, 4) : 0)).ToList();
            var p = ProductivityCalculator.Compute(time, approved, notApproved, weights);
            var first = g.First().Device;

            rows.Add(new ReportRowDto(
                g.Key.Key, g.Key.Name,
                groupBy == "employee" ? first.Employee?.Position : null,
                groupBy == "department" ? g.Key.Name : first.Employee?.Department?.Name,
                g.Count(),
                Math.Round(time.ScheduledSeconds), Math.Round(time.ActiveInScheduleSeconds), Math.Round(time.IdleInScheduleSeconds),
                Math.Round(time.PausedInScheduleSeconds), Math.Round(time.ScheduledBreakSeconds), Math.Round(time.OutsideScheduleSeconds),
                Math.Round(time.UnknownSeconds), p.Coverage, p.Confidence, p.Score, p.IsReliable, p.Metrics, top));
        }

        return rows.OrderBy(r => r.DepartmentName).ThenBy(r => r.Position).ThenBy(r => r.GroupName).ToList();
    }

    public static List<DailyActivityPointDto> Daily(ReportData data)
    {
        var points = new List<DailyActivityPointDto>();
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(data.From, data.TimeZone));
        while (true)
        {
            var start = LocalMidnightUtc(day, data.TimeZone);
            if (start >= data.To) break;
            var end = LocalMidnightUtc(day.AddDays(1), data.TimeZone);
            var s = start < data.From ? data.From : start;
            var e = end > data.To ? data.To : end;
            var total = data.Devices.Select(d => ScheduleCalculator.Classify(d.Sessions, d.Schedule, s, e)).Aggregate(new TimeBreakdown(), (a, b) => a + b);
            points.Add(new DailyActivityPointDto(day, Math.Round(total.ActiveTotalSeconds),
                Math.Round(total.IdleInScheduleSeconds), Math.Round(total.UnknownSeconds)));
            day = day.AddDays(1);
        }
        return points;
    }

    private async Task<WorkSchedule> DefaultScheduleAsync(SystemSettingsDto s, CancellationToken ct)
    {
        var dto = await policies.DefaultScheduleDtoAsync(ct);
        return dto is not null ? WorkScheduleMapping.ToEntity(dto) : new WorkSchedule { Name = "Default", TimeZoneId = s.OrganizationTimeZone };
    }
}
