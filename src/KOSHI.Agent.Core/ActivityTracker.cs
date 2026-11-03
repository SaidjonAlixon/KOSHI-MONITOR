using KOSHI.Contracts;
using KOSHI.Contracts.Privacy;

namespace KOSHI.Agent.Core;

/// <summary>Foreground window snapshot. ProcessName is the executable file name only (no path).</summary>
public sealed record ForegroundInfo(string ProcessName, string? WindowTitle, int ProcessId);

public sealed record TrackerSettings(int IdleThresholdSeconds, WindowTitleCaptureDto TitleCapture)
{
    public static readonly TrackerSettings Default = new(300, WindowTitleCaptureDto.None);
}

public sealed record CurrentActivity(string? ProcessName, string? WindowTitle, ActivityStateDto State, DateTime Since);

/// <summary>
/// Turns periodic samples (foreground window + seconds since last input) into de-duplicated segments:
/// consecutive samples of the same process/title extend one segment instead of producing new events.
/// Deterministic and free of OS calls so it can be unit-tested.
/// </summary>
public sealed class ActivityTracker
{
    private readonly List<ApplicationEventDto> _events = [];
    private readonly List<ActivitySessionDto> _sessions = [];
    private Segment? _app;
    private (ActivityStateDto State, DateTime Start, DateTime Last)? _state;
    private DateTime? _lastSample;

    public TrackerSettings Settings { get; set; } = TrackerSettings.Default;

    /// <summary>Gap after which a missing sample (sleep, hibernate, agent stopped) closes the open segments.</summary>
    public TimeSpan MaxSampleGap { get; set; } = TimeSpan.FromSeconds(30);

    public CurrentActivity? Current { get; private set; }

    public void Sample(ForegroundInfo? fg, double idleSeconds, DateTime nowUtc, bool monitoringAllowed = true)
    {
        if (_lastSample is { } last && nowUtc - last > MaxSampleGap) Flush(last);
        _lastSample = nowUtc;

        if (!monitoringAllowed)
        {
            UpdateState(ActivityStateDto.Paused, nowUtc);
            CloseApp(nowUtc);
            Current = new CurrentActivity(null, null, ActivityStateDto.Paused, _state!.Value.Start);
            return;
        }

        var idle = idleSeconds >= Settings.IdleThresholdSeconds;
        // Idle begins when input stopped, not when the threshold was crossed.
        var stateTime = idle && _state?.State != ActivityStateDto.Idle ? nowUtc.AddSeconds(-idleSeconds) : nowUtc;
        UpdateState(idle ? ActivityStateDto.Idle : ActivityStateDto.Active, stateTime);

        if (idle || fg is null)
        {
            CloseApp(idle ? nowUtc.AddSeconds(-idleSeconds) : nowUtc);
        }
        else
        {
            var title = WindowTitleMasker.Apply(fg.WindowTitle, Settings.TitleCapture);
            if (_app is { } a && a.ProcessName == fg.ProcessName && a.Title == title)
            {
                a.Last = nowUtc;
            }
            else
            {
                CloseApp(nowUtc);
                _app = new Segment(fg.ProcessName, title, fg.ProcessId, nowUtc) { Last = nowUtc };
            }
        }

        Current = new CurrentActivity(_app?.ProcessName, _app?.Title, _state!.Value.State, _app?.Start ?? _state.Value.Start);
    }

    /// <summary>Closes open segments at <paramref name="at"/> (e.g. on shutdown or before a long gap).</summary>
    public void Flush(DateTime at)
    {
        CloseApp(at);
        if (_state is { } s && at > s.Start) AddSession(s.State, s.Start, at);
        _state = null;
    }

    /// <summary>Returns and clears completed segments. Open segments are split at <paramref name="now"/> so long sessions are reported periodically.</summary>
    public (List<ApplicationEventDto> Events, List<ActivitySessionDto> Sessions) Drain(DateTime now, TimeSpan splitOpenAfter)
    {
        if (_app is { } a && now - a.Start >= splitOpenAfter)
        {
            CloseApp(now);
            _app = new Segment(a.ProcessName, a.Title, a.ProcessId, now) { Last = now };
        }
        if (_state is { } s && now - s.Start >= splitOpenAfter)
        {
            AddSession(s.State, s.Start, now);
            _state = (s.State, now, now);
        }

        var result = (_events.ToList(), _sessions.ToList());
        _events.Clear();
        _sessions.Clear();
        return result;
    }

    private void UpdateState(ActivityStateDto state, DateTime at)
    {
        if (_state is { } s && s.State == state) return;
        if (_state is { } prev && at > prev.Start) AddSession(prev.State, prev.Start, at);
        _state = (state, at, at);
    }

    private void CloseApp(DateTime end)
    {
        if (_app is not { } a) return;
        var e = end < a.Start ? a.Start : end;
        if ((e - a.Start).TotalSeconds >= 1)
            _events.Add(new ApplicationEventDto
            {
                IdempotencyKey = $"ev-{Guid.NewGuid():N}",
                EventType = ApplicationEventTypeDto.Foreground,
                ProcessName = a.ProcessName,
                ExecutableName = a.ProcessName,
                WindowTitle = a.Title,
                ProcessId = a.ProcessId,
                StartedAt = a.Start,
                EndedAt = e,
            });
        _app = null;
    }

    private void AddSession(ActivityStateDto state, DateTime start, DateTime end)
    {
        if ((end - start).TotalSeconds < 1) return;
        _sessions.Add(new ActivitySessionDto { IdempotencyKey = $"as-{Guid.NewGuid():N}", State = state, StartedAt = start, EndedAt = end });
    }

    public void AddViolation(string processName, DateTime at, string reason)
    {
        _ = reason;
        _events.Add(new ApplicationEventDto
        {
            IdempotencyKey = $"pv-{Guid.NewGuid():N}",
            EventType = ApplicationEventTypeDto.PolicyViolation,
            ProcessName = processName,
            ExecutableName = processName,
            StartedAt = at,
            EndedAt = at,
        });
    }

    private sealed class Segment(string processName, string? title, int processId, DateTime start)
    {
        public string ProcessName { get; } = processName;
        public string? Title { get; } = title;
        public int ProcessId { get; } = processId;
        public DateTime Start { get; } = start;
        public DateTime Last { get; set; }
    }
}
