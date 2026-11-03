using KOSHI.Agent.Core;
using KOSHI.Contracts;

namespace KOSHI.Agent.Tests;

public sealed class ActivityTrackerTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Continuous_same_window_produces_a_single_event()
    {
        var t = new ActivityTracker { Settings = new TrackerSettings(300, WindowTitleCaptureDto.None) };
        var fg = new ForegroundInfo("word.exe", "Doc", 10);
        for (var i = 0; i <= 20; i++) t.Sample(fg, 0, T0.AddSeconds(i * 3));
        t.Flush(T0.AddSeconds(60));

        var (events, sessions) = t.Drain(T0.AddSeconds(60), TimeSpan.FromHours(1));
        var e = Assert.Single(events);
        Assert.Equal("word.exe", e.ProcessName);
        Assert.Equal(60, (e.EndedAt - e.StartedAt).TotalSeconds);
        Assert.Null(e.WindowTitle);
        Assert.Equal(ActivityStateDto.Active, Assert.Single(sessions).State);
    }

    [Fact]
    public void Switching_application_closes_previous_segment()
    {
        var t = new ActivityTracker();
        t.Sample(new ForegroundInfo("a.exe", null, 1), 0, T0);
        t.Sample(new ForegroundInfo("a.exe", null, 1), 0, T0.AddSeconds(3));
        t.Sample(new ForegroundInfo("b.exe", null, 2), 0, T0.AddSeconds(6));
        t.Flush(T0.AddSeconds(9));
        var (events, _) = t.Drain(T0.AddSeconds(9), TimeSpan.FromHours(1));
        Assert.Equal(["a.exe", "b.exe"], events.Select(e => e.ProcessName));
        Assert.Equal(T0.AddSeconds(6), events[0].EndedAt);
    }

    [Fact]
    public void Idle_starts_when_input_stopped_and_is_not_counted_as_application_time()
    {
        var t = new ActivityTracker { Settings = new TrackerSettings(60, WindowTitleCaptureDto.None) };
        var fg = new ForegroundInfo("excel.exe", null, 5);
        for (var s = 0; s <= 90; s += 3) t.Sample(fg, s, T0.AddSeconds(s));   // no input after T0
        t.Flush(T0.AddSeconds(120));

        var (events, sessions) = t.Drain(T0.AddSeconds(120), TimeSpan.FromHours(1));
        Assert.Empty(events.Where(e => (e.EndedAt - e.StartedAt).TotalSeconds > 1));
        var idle = Assert.Single(sessions, s => s.State == ActivityStateDto.Idle);
        Assert.Equal(T0, idle.StartedAt);
    }

    [Fact]
    public void Masked_titles_hide_personal_data()
    {
        var t = new ActivityTracker { Settings = new TrackerSettings(300, WindowTitleCaptureDto.Masked) };
        t.Sample(new ForegroundInfo("outlook.exe", "Inbox - ali@example.com", 1), 0, T0);
        t.Flush(T0.AddSeconds(10));
        var e = Assert.Single(t.Drain(T0.AddSeconds(10), TimeSpan.FromHours(1)).Events);
        Assert.DoesNotContain("ali@example.com", e.WindowTitle);
    }

    [Fact]
    public void Long_gap_between_samples_closes_open_segments_at_last_sample()
    {
        var t = new ActivityTracker();
        var fg = new ForegroundInfo("a.exe", null, 1);
        t.Sample(fg, 0, T0);
        t.Sample(fg, 0, T0.AddSeconds(3));
        t.Sample(fg, 0, T0.AddHours(2));   // PC was asleep
        var (events, _) = t.Drain(T0.AddHours(2), TimeSpan.FromHours(1));
        Assert.Equal(T0.AddSeconds(3), Assert.Single(events).EndedAt);
    }

    [Fact]
    public void Drain_splits_long_open_segments_with_unique_idempotency_keys()
    {
        var t = new ActivityTracker { MaxSampleGap = TimeSpan.FromHours(1) };
        var fg = new ForegroundInfo("a.exe", null, 1);
        t.Sample(fg, 0, T0);
        t.Sample(fg, 0, T0.AddMinutes(6));
        var first = t.Drain(T0.AddMinutes(6), TimeSpan.FromMinutes(5));
        t.Sample(fg, 0, T0.AddMinutes(6).AddSeconds(3));
        t.Flush(T0.AddMinutes(7));
        var second = t.Drain(T0.AddMinutes(7), TimeSpan.FromMinutes(5));
        var all = first.Events.Concat(second.Events).ToList();
        Assert.Equal(2, all.Count);
        Assert.Equal(all.Count, all.Select(e => e.IdempotencyKey).Distinct().Count());
        Assert.Equal(all[0].EndedAt, all[1].StartedAt);
    }

    [Fact]
    public void Paused_monitoring_does_not_record_applications()
    {
        var t = new ActivityTracker();
        t.Sample(new ForegroundInfo("a.exe", "secret", 1), 0, T0, monitoringAllowed: false);
        t.Flush(T0.AddSeconds(30));
        var (events, sessions) = t.Drain(T0.AddSeconds(30), TimeSpan.FromHours(1));
        Assert.Empty(events);
        Assert.Equal(ActivityStateDto.Paused, Assert.Single(sessions).State);
    }
}

public sealed class OfflineQueueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "koshi-queue-" + Guid.NewGuid().ToString("N"));

    private static ApplicationEventDto Ev(int i) => new()
    {
        IdempotencyKey = $"ev-test-{i:D6}", ProcessName = "a.exe",
        StartedAt = DateTime.UtcNow, EndedAt = DateTime.UtcNow.AddSeconds(1),
    };

    [Fact]
    public void Batches_survive_restart_and_are_removed_after_upload()
    {
        new OfflineQueue(_dir).Enqueue([Ev(1), Ev(2)], [], DateTime.UtcNow);
        var reopened = new OfflineQueue(_dir);
        var item = reopened.Peek();
        Assert.NotNull(item);
        Assert.Equal(2, item!.Value.Batch.ApplicationEvents.Count);
        reopened.Remove(item.Value.Path);
        Assert.Null(reopened.Peek());
    }

    [Fact]
    public void Large_input_is_split_into_server_sized_batches()
    {
        var q = new OfflineQueue(_dir);
        q.Enqueue(Enumerable.Range(0, AgentLimits.MaxEventsPerBatch * 2 + 1).Select(Ev).ToList(), [], DateTime.UtcNow);
        Assert.Equal(3, q.Count);
    }

    [Fact]
    public void Oldest_batches_are_dropped_when_limit_is_reached()
    {
        var q = new OfflineQueue(_dir, maxBatches: 3);
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++) q.Enqueue([Ev(i)], [], now.AddMilliseconds(i));
        Assert.Equal(3, q.Count);
        Assert.Equal(2, q.Dropped);
        Assert.Equal("ev-test-000002", q.Peek()!.Value.Batch.ApplicationEvents[0].IdempotencyKey);
    }

    [Fact]
    public void Corrupt_files_are_discarded()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "00000000000000000-bad.json"), "{not json");
        var q = new OfflineQueue(_dir);
        q.Enqueue([Ev(1)], [], DateTime.UtcNow);
        Assert.Equal("ev-test-000001", q.Peek()!.Value.Batch.ApplicationEvents[0].IdempotencyKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
