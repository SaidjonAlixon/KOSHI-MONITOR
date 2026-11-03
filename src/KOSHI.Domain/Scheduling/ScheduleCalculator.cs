namespace KOSHI.Domain.Scheduling;

public readonly record struct UtcInterval(DateTime Start, DateTime End)
{
    public double Seconds => Math.Max(0, (End - Start).TotalSeconds);

    public UtcInterval? Intersect(UtcInterval other)
    {
        var s = Start > other.Start ? Start : other.Start;
        var e = End < other.End ? End : other.End;
        return e > s ? new UtcInterval(s, e) : null;
    }
}

public readonly record struct StateInterval(ActivityState State, DateTime Start, DateTime End);

/// <summary>Time buckets reported separately; idle time is never folded into "unproductive".</summary>
public sealed record TimeBreakdown
{
    public double ScheduledSeconds { get; init; }
    public double ActiveInScheduleSeconds { get; init; }
    public double IdleInScheduleSeconds { get; init; }
    public double PausedInScheduleSeconds { get; init; }
    public double ScheduledBreakSeconds { get; init; }
    public double OutsideScheduleSeconds { get; init; }
    public double ActiveTotalSeconds { get; init; }

    /// <summary>Scheduled work time for which the agent reported any state (active, idle or paused).</summary>
    public double CoveredScheduledSeconds => ActiveInScheduleSeconds + IdleInScheduleSeconds + PausedInScheduleSeconds;

    /// <summary>Scheduled work time with no telemetry at all (device off, agent offline, data lost).</summary>
    public double UnknownSeconds => Math.Max(0, ScheduledSeconds - CoveredScheduledSeconds);

    public static TimeBreakdown operator +(TimeBreakdown a, TimeBreakdown b) => new()
    {
        ScheduledSeconds = a.ScheduledSeconds + b.ScheduledSeconds,
        ActiveInScheduleSeconds = a.ActiveInScheduleSeconds + b.ActiveInScheduleSeconds,
        IdleInScheduleSeconds = a.IdleInScheduleSeconds + b.IdleInScheduleSeconds,
        PausedInScheduleSeconds = a.PausedInScheduleSeconds + b.PausedInScheduleSeconds,
        ScheduledBreakSeconds = a.ScheduledBreakSeconds + b.ScheduledBreakSeconds,
        OutsideScheduleSeconds = a.OutsideScheduleSeconds + b.OutsideScheduleSeconds,
        ActiveTotalSeconds = a.ActiveTotalSeconds + b.ActiveTotalSeconds,
    };
}

public static class ScheduleCalculator
{
    public static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>Scheduled work intervals (excluding the break) that intersect [from, to).</summary>
    public static List<UtcInterval> WorkIntervals(WorkSchedule s, DateTime fromUtc, DateTime toUtc) =>
        BuildIntervals(s, fromUtc, toUtc, breaks: false);

    /// <summary>Scheduled break intervals that intersect [from, to).</summary>
    public static List<UtcInterval> BreakIntervals(WorkSchedule s, DateTime fromUtc, DateTime toUtc) =>
        BuildIntervals(s, fromUtc, toUtc, breaks: true);

    public static bool IsWithinWorkTime(WorkSchedule s, DateTime utc) =>
        WorkIntervals(s, utc.AddDays(-2), utc.AddDays(2)).Any(i => i.Start <= utc && utc < i.End);

    public static TimeBreakdown Classify(IEnumerable<StateInterval> sessions, WorkSchedule schedule, DateTime fromUtc, DateTime toUtc)
    {
        var work = WorkIntervals(schedule, fromUtc, toUtc);
        var breaks = BreakIntervals(schedule, fromUtc, toUtc);
        var window = new UtcInterval(fromUtc, toUtc);

        double active = 0, idle = 0, paused = 0, inBreak = 0, outside = 0, activeTotal = 0;
        foreach (var session in Normalize(sessions))
        {
            if (window.Intersect(new UtcInterval(session.Start, session.End)) is not { } clipped) continue;

            var inWork = Overlap(clipped, work);
            var inBrk = Overlap(clipped, breaks);
            var rest = Math.Max(0, clipped.Seconds - inWork - inBrk);

            switch (session.State)
            {
                case ActivityState.Active: active += inWork; activeTotal += clipped.Seconds; break;
                case ActivityState.Idle: idle += inWork; break;
                case ActivityState.Paused: paused += inWork; break;
            }

            if (session.State != ActivityState.Paused)
            {
                inBreak += inBrk;
                outside += rest;
            }
        }

        return new TimeBreakdown
        {
            ScheduledSeconds = work.Sum(i => i.Seconds),
            ActiveInScheduleSeconds = active,
            IdleInScheduleSeconds = idle,
            PausedInScheduleSeconds = paused,
            ScheduledBreakSeconds = inBreak,
            OutsideScheduleSeconds = outside,
            ActiveTotalSeconds = activeTotal,
        };
    }

    /// <summary>Sorts sessions and trims overlaps so that no second is counted twice.</summary>
    public static IEnumerable<StateInterval> Normalize(IEnumerable<StateInterval> sessions)
    {
        DateTime? cursor = null;
        foreach (var s in sessions.Where(x => x.End > x.Start).OrderBy(x => x.Start).ThenBy(x => x.End))
        {
            var start = cursor is { } c && c > s.Start ? c : s.Start;
            if (s.End <= start) continue;
            cursor = s.End;
            yield return s with { Start = start };
        }
    }

    private static double Overlap(UtcInterval x, List<UtcInterval> intervals)
    {
        double total = 0;
        foreach (var i in intervals)
        {
            if (x.Intersect(i) is { } o) total += o.Seconds;
        }
        return total;
    }

    private static List<UtcInterval> BuildIntervals(WorkSchedule s, DateTime fromUtc, DateTime toUtc, bool breaks)
    {
        var tz = ResolveTimeZone(s.TimeZoneId);
        var result = new List<UtcInterval>();
        var window = new UtcInterval(fromUtc, toUtc);
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(fromUtc, tz)).AddDays(-1);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(toUtc, tz));

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (!WorkDays.Contains(s.WorkDaysMask, day.DayOfWeek)) continue;

            var workStart = ToUtc(day, s.WorkStart, tz);
            var workEnd = ToUtc(s.WorkEnd > s.WorkStart ? day : day.AddDays(1), s.WorkEnd, tz);
            if (workEnd <= workStart) continue;

            UtcInterval? brk = null;
            if (s.BreakStart is { } bs && s.BreakEnd is { } be)
            {
                var bStartDay = bs >= s.WorkStart ? day : day.AddDays(1);
                var bStart = ToUtc(bStartDay, bs, tz);
                var bEnd = ToUtc(be > bs ? bStartDay : bStartDay.AddDays(1), be, tz);
                brk = new UtcInterval(bStart, bEnd).Intersect(new UtcInterval(workStart, workEnd));
            }

            var segments = new List<UtcInterval>();
            if (breaks)
            {
                if (brk is { } b) segments.Add(b);
            }
            else if (brk is { } b)
            {
                segments.Add(new UtcInterval(workStart, b.Start));
                segments.Add(new UtcInterval(b.End, workEnd));
            }
            else
            {
                segments.Add(new UtcInterval(workStart, workEnd));
            }

            foreach (var seg in segments)
            {
                if (seg.Seconds > 0 && window.Intersect(seg) is { } clipped) result.Add(clipped);
            }
        }

        return result;
    }

    private static DateTime ToUtc(DateOnly day, TimeOnly time, TimeZoneInfo tz)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(time), DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }
}
