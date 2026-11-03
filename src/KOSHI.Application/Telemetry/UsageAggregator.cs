using KOSHI.Domain;

namespace KOSHI.Application.Telemetry;

public readonly record struct UsageKey(DateOnly Date, string ProcessName);

public sealed record UsageDelta(long ForegroundSeconds, long BackgroundSeconds, int SessionCount);

/// <summary>Splits application events across local calendar days and sums durations per (date, process).</summary>
public static class UsageAggregator
{
    public static Dictionary<UsageKey, UsageDelta> Aggregate(IEnumerable<ApplicationEvent> events, TimeZoneInfo tz)
    {
        var result = new Dictionary<UsageKey, UsageDelta>();
        foreach (var e in events)
        {
            if (e.EventType == ApplicationEventType.PolicyViolation || e.EndedAt <= e.StartedAt) continue;
            var first = true;
            foreach (var (date, seconds) in SplitByLocalDay(e.StartedAt, e.EndedAt, tz))
            {
                var key = new UsageKey(date, e.ProcessName);
                var fg = e.EventType == ApplicationEventType.Foreground ? seconds : 0;
                var bg = e.EventType == ApplicationEventType.Background ? seconds : 0;
                var sessions = first && e.EventType == ApplicationEventType.Foreground ? 1 : 0;
                result[key] = result.TryGetValue(key, out var d)
                    ? new UsageDelta(d.ForegroundSeconds + fg, d.BackgroundSeconds + bg, d.SessionCount + sessions)
                    : new UsageDelta(fg, bg, sessions);
                first = false;
            }
        }
        return result;
    }

    public static IEnumerable<(DateOnly Date, long Seconds)> SplitByLocalDay(DateTime startUtc, DateTime endUtc, TimeZoneInfo tz)
    {
        var cursor = startUtc;
        while (cursor < endUtc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(cursor, tz);
            var date = DateOnly.FromDateTime(local);
            var nextMidnightLocal = DateTime.SpecifyKind(date.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            var nextMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(nextMidnightLocal, tz);
            var segmentEnd = nextMidnightUtc < endUtc ? nextMidnightUtc : endUtc;
            var seconds = (long)Math.Round((segmentEnd - cursor).TotalSeconds);
            if (seconds > 0) yield return (date, seconds);
            cursor = segmentEnd;
        }
    }
}
