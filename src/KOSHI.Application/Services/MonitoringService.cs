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
            var connectedSince = d.ConnectedAt;
            return new LiveDeviceDto(
                d.Id, d.DisplayName, d.Employee?.FullName, d.Employee?.Department?.Name,
                online ? d.CurrentProcessName : null,
                online ? d.CurrentWindowTitle : null,
                online ? d.CurrentState?.ToString() : null,
                started,
                started is { } st ? (int)Math.Max(0, (now - st).TotalSeconds) : null,
                d.LastHeartbeatAt, connectedSince, d.DisconnectedAt,
                connectedSince is { } cs ? (int)Math.Max(0, (now - cs).TotalSeconds) : null,
                status, d.MonitoringEnabled && online);
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

    /// <summary>Full activity of a single computer for a period: power on/off, active vs idle, apps, windows and browsing.</summary>
    public async Task<ActivityDetailDto> ActivityDetailAsync(MonitoringQuery q, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var tz = ScheduleCalculator.ResolveTimeZone(s.OrganizationTimeZone);
        var (from, to) = Range(q);
        from = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        to = DateTime.SpecifyKind(to, DateTimeKind.Utc);

        var devices = await ScopedDevices(q).OrderBy(d => d.DisplayName).Take(2).ToListAsync(ct);
        if (devices.Count == 0) throw Errors.NotFound("Qurilma");
        if (devices.Count > 1) throw Errors.Validation("deviceId", "Bitta kompyuterni tanlang — tafsilotlar faqat bitta kompyuter uchun ko‘rsatiladi.");
        var d = devices[0];

        var conns = await db.DeviceConnections.AsNoTracking()
            .Where(c => c.DeviceId == d.Id && c.ConnectedAt < to && (c.DisconnectedAt == null || c.DisconnectedAt > from))
            .OrderBy(c => c.ConnectedAt)
            .Select(c => new { c.ConnectedAt, c.DisconnectedAt })
            .ToListAsync(ct);
        var powerSessions = conns.Select(c =>
        {
            var ongoing = c.DisconnectedAt is null;
            var off = c.DisconnectedAt;
            var clampStart = c.ConnectedAt < from ? from : c.ConnectedAt;
            var clampEnd = (off ?? to) > to ? to : (off ?? to);
            var dur = (int)Math.Max(0, (clampEnd - clampStart).TotalSeconds);
            return new PowerSessionDto(c.ConnectedAt, off, ongoing && off is null ? null : dur, ongoing);
        }).ToList();
        var onlineSeconds = conns.Sum(c =>
        {
            var start = c.ConnectedAt < from ? from : c.ConnectedAt;
            var end = (c.DisconnectedAt ?? to) > to ? to : (c.DisconnectedAt ?? to);
            return Math.Max(0, (end - start).TotalSeconds);
        });

        var sessions = await db.ActivitySessions.AsNoTracking()
            .Where(x => x.DeviceId == d.Id && x.EndedAt > from && x.StartedAt < to)
            .Select(x => new { x.State, x.StartedAt, x.EndedAt })
            .ToListAsync(ct);
        double ClampSeconds(DateTime a, DateTime b) =>
            Math.Max(0, ((b > to ? to : b) - (a < from ? from : a)).TotalSeconds);
        var activeSeconds = sessions.Where(x => x.State == ActivityState.Active).Sum(x => ClampSeconds(x.StartedAt, x.EndedAt));
        var idleSeconds = sessions.Where(x => x.State != ActivityState.Active).Sum(x => ClampSeconds(x.StartedAt, x.EndedAt));

        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(from, tz));
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(to.AddTicks(-1), tz));
        var usageRows = await db.ApplicationUsageSummaries.AsNoTracking()
            .Where(u => u.DeviceId == d.Id && u.Date >= firstDate && u.Date <= lastDate)
            .GroupBy(u => u.ProcessName)
            .Select(g => new { ProcessName = g.Key, Fg = g.Sum(x => x.ForegroundSeconds), Bg = g.Sum(x => x.BackgroundSeconds), S = g.Sum(x => x.SessionCount) })
            .OrderByDescending(x => x.Fg).ToListAsync(ct);
        var totalFg = usageRows.Sum(r => (double)r.Fg);
        var apps = usageRows.Take(50)
            .Select(r => new ApplicationUsageDto(r.ProcessName, r.Fg, r.Bg, r.S, totalFg > 0 ? Math.Round(r.Fg / totalFg, 4) : 0))
            .ToList();

        // Load then group in memory: Npgsql cannot translate GroupBy + DTO / (long) casts.
        var windowEvents = await db.ApplicationEvents.AsNoTracking()
            .Where(e => e.DeviceId == d.Id && e.WindowTitle != null && e.EndedAt > from && e.StartedAt < to)
            .Select(e => new { e.EventType, e.ProcessName, e.WindowTitle, e.DurationSeconds, e.StartedAt, e.EndedAt })
            .ToListAsync(ct);
        static List<WindowUsageDto> GroupWindows(IEnumerable<(string ProcessName, string? Title, int Duration, DateTime StartedAt, DateTime EndedAt)> rows, int take) =>
            rows.GroupBy(e => (e.ProcessName, e.Title))
                .Select(g => new WindowUsageDto(g.Key.ProcessName, g.Key.Title, g.Sum(x => (long)x.Duration), g.Count(), g.Min(x => x.StartedAt), g.Max(x => x.EndedAt)))
                .OrderByDescending(x => x.Seconds).Take(take).ToList();
        var fgGroups = GroupWindows(
            windowEvents.Where(e => e.EventType == ApplicationEventType.Foreground)
                .Select(e => (e.ProcessName, e.WindowTitle, e.DurationSeconds, e.StartedAt, e.EndedAt)), 400);
        var bgWindows = GroupWindows(
            windowEvents.Where(e => e.EventType == ApplicationEventType.Background)
                .Select(e => (e.ProcessName, e.WindowTitle, e.DurationSeconds, e.StartedAt, e.EndedAt)), 60);

        var foregroundWindows = fgGroups.Take(80).ToList();

        var browsing = fgGroups
            .Where(w => IsBrowser(w.ProcessName) && !string.IsNullOrWhiteSpace(w.WindowTitle))
            .Select(w => (Window: w, Parsed: ParseBrowserTitle(w.WindowTitle!)))
            .GroupBy(x => (Browser: BrowserName(x.Window.ProcessName), x.Parsed.Title, x.Parsed.Site, x.Parsed.Query, x.Parsed.Kind))
            .Select(g => new BrowsingItemDto(
                g.Key.Browser, g.Key.Title, g.Key.Site, g.Key.Query, g.Key.Kind,
                g.Sum(x => x.Window.Seconds), g.Sum(x => x.Window.Count),
                g.Min(x => x.Window.FirstAt), g.Max(x => x.Window.LastAt),
                GuessBrowseUrl(g.Key.Site, g.Key.Query, g.Key.Kind, g.Key.Title)))
            .OrderByDescending(b => b.Seconds).Take(120).ToList();

        var firstActivity = sessions.Count > 0 ? sessions.Min(x => x.StartedAt) : (DateTime?)null;
        var lastActivity = sessions.Count > 0 ? sessions.Max(x => x.EndedAt) : (DateTime?)null;
        if (conns.Count > 0)
        {
            var connFirst = conns.Min(c => c.ConnectedAt);
            firstActivity = firstActivity is { } fa && fa < connFirst ? fa : connFirst;
            var connLast = conns.Max(c => c.DisconnectedAt ?? to);
            lastActivity = lastActivity is { } la && la > connLast ? la : connLast;
        }

        var totals = new ActivityTotalsDto(
            Math.Round(activeSeconds), Math.Round(idleSeconds), Math.Round(onlineSeconds),
            firstActivity, lastActivity, powerSessions.Count, usageRows.Count, browsing.Count);

        return new ActivityDetailDto(
            d.Id, d.DisplayName, d.Hostname, d.Employee?.FullName, d.Employee?.Department?.Name,
            from, to, tz.Id, totals, powerSessions, apps, foregroundWindows, bgWindows, browsing);
    }

    private static readonly Dictionary<string, string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome.exe"] = "Google Chrome",
        ["msedge.exe"] = "Microsoft Edge",
        ["firefox.exe"] = "Mozilla Firefox",
        ["opera.exe"] = "Opera",
        ["opera_gx.exe"] = "Opera GX",
        ["brave.exe"] = "Brave",
        ["vivaldi.exe"] = "Vivaldi",
        ["browser.exe"] = "Yandex Browser",
        ["yandex.exe"] = "Yandex Browser",
        ["iexplore.exe"] = "Internet Explorer",
    };

    private static readonly string[] BrowserSuffixes =
    [
        "Google Chrome", "Microsoft\u200b Edge", "Microsoft Edge", "Mozilla Firefox", "Firefox",
        "Opera", "Opera GX", "Brave", "Vivaldi", "Yandex", "Internet Explorer",
    ];

    public static bool IsBrowser(string processName) => BrowserProcesses.ContainsKey(processName);

    private static string BrowserName(string processName) => BrowserProcesses.GetValueOrDefault(processName, processName);

    /// <summary>
    /// Turns a browser window title into a clean page title plus, where possible, the site and the search query.
    /// Works on both full and masked titles (masked titles already have URLs/numbers stripped).
    /// </summary>
    public static (string Title, string? Site, string? Query, string Kind) ParseBrowserTitle(string raw)
    {
        var t = raw.Trim();
        // "(3) Title" notification badge → drop the count.
        var paren = System.Text.RegularExpressions.Regex.Replace(t, @"^\(\d+\)\s*", "");
        t = paren.Length > 0 ? paren : t;

        foreach (var suffix in BrowserSuffixes)
        {
            var tail = " - " + suffix;
            if (t.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
            {
                t = t[..^tail.Length].Trim();
                break;
            }
        }
        if (t.Length == 0) t = raw.Trim();

        (string? site, string? query, string kind) Detect(string marker, string site, string kind)
        {
            var tail = " - " + marker;
            if (t.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
            {
                var head = t[..^tail.Length].Trim();
                return (site, kind == "qidiruv" ? head : null, kind);
            }
            return (null, null, "");
        }

        foreach (var (marker, site, kind) in new[]
                 {
                     ("YouTube", "YouTube", "video"),
                     ("Google Search", "Google", "qidiruv"),
                     ("Google qidiruvi", "Google", "qidiruv"),
                     ("Qidiruv - Google", "Google", "qidiruv"),
                     ("Поиск в Google", "Google", "qidiruv"),
                     ("Bing", "Bing", "qidiruv"),
                     ("Yandex", "Yandex", "qidiruv"),
                 })
        {
            var (s, qd, k) = Detect(marker, site, kind);
            if (k.Length > 0)
            {
                var title = k == "qidiruv" && !string.IsNullOrWhiteSpace(qd) ? qd! : t;
                return (Trim128(title), s, qd, k);
            }
        }

        return (Trim128(t), null, null, "sahifa");
    }

    private static string? GuessBrowseUrl(string? site, string? query, string kind, string title)
    {
        if (kind == "qidiruv" && !string.IsNullOrWhiteSpace(query))
        {
            var q = Uri.EscapeDataString(query);
            return site switch
            {
                "Google" => "https://www.google.com/search?q=" + q,
                "Yandex" => "https://yandex.ru/search/?text=" + q,
                "Bing" => "https://www.bing.com/search?q=" + q,
                _ => "https://www.google.com/search?q=" + q,
            };
        }
        if (kind == "video" && site == "YouTube")
            return string.IsNullOrWhiteSpace(query)
                ? "https://www.youtube.com"
                : "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query);
        var m = System.Text.RegularExpressions.Regex.Match(title, @"https?://[^\s<>""']+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return m.Value;
        if (!string.IsNullOrWhiteSpace(site) && site is not ("Google" or "YouTube" or "Bing" or "Yandex"))
            return "https://" + (site.Contains('.', StringComparison.Ordinal) ? site : site.ToLowerInvariant() + ".com");
        return null;
    }

    private static string Trim128(string s) => s.Length <= 128 ? s : s[..128];

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
