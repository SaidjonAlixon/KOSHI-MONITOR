using System.Text.Json;
using KOSHI.Contracts;

namespace KOSHI.Agent.Core;

public sealed record QueuedBatch(Guid BatchId, List<ApplicationEventDto> ApplicationEvents, List<ActivitySessionDto> ActivitySessions, DateTime CreatedAt);

/// <summary>
/// Bounded, crash-safe local queue: one JSON file per batch, written atomically. Oldest batches are dropped
/// when the item limit or maximum age is exceeded so that a long outage cannot fill the disk.
/// </summary>
public sealed class OfflineQueue
{
    private readonly string _dir;
    private readonly int _maxBatches;
    private readonly TimeSpan _maxAge;
    private readonly Lock _gate = new();

    public OfflineQueue(string directory, int maxBatches = 5000, TimeSpan? maxAge = null)
    {
        _dir = directory;
        _maxBatches = maxBatches;
        _maxAge = maxAge ?? TimeSpan.FromDays(7);
        Directory.CreateDirectory(_dir);
    }

    public int Count
    {
        get
        {
            lock (_gate) return Directory.EnumerateFiles(_dir, "*.json").Count();
        }
    }

    public int Dropped { get; private set; }

    public void Enqueue(IReadOnlyCollection<ApplicationEventDto> events, IReadOnlyCollection<ActivitySessionDto> sessions, DateTime now)
    {
        if (events.Count == 0 && sessions.Count == 0) return;
        lock (_gate)
        {
            foreach (var chunk in Chunk(events.ToList(), sessions.ToList()))
            {
                var batch = new QueuedBatch(Guid.NewGuid(), chunk.Events, chunk.Sessions, now);
                var name = $"{now:yyyyMMddHHmmssfff}-{batch.BatchId:N}.json";
                var tmp = Path.Combine(_dir, name + ".tmp");
                File.WriteAllText(tmp, JsonSerializer.Serialize(batch, KoshiApiClient.Json));
                File.Move(tmp, Path.Combine(_dir, name));
            }
            Trim(now);
        }
    }

    /// <summary>Returns the oldest batch without removing it; call <see cref="Remove"/> after a successful upload.</summary>
    public (string Path, QueuedBatch Batch)? Peek()
    {
        lock (_gate)
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    var batch = JsonSerializer.Deserialize<QueuedBatch>(File.ReadAllText(file), KoshiApiClient.Json);
                    if (batch is not null) return (file, batch);
                }
                catch (JsonException)
                {
                    // Corrupt file (e.g. power loss mid-write of an old version): discard.
                }
                File.Delete(file);
            }
            return null;
        }
    }

    public void Remove(string path)
    {
        lock (_gate)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private void Trim(DateTime now)
    {
        var files = Directory.EnumerateFiles(_dir, "*.json").Order(StringComparer.Ordinal).ToList();
        var excess = files.Count - _maxBatches;
        foreach (var f in files)
        {
            var tooOld = now - File.GetCreationTimeUtc(f) > _maxAge;
            if (excess <= 0 && !tooOld) break;
            File.Delete(f);
            excess--;
            Dropped++;
        }
    }

    private static IEnumerable<(List<ApplicationEventDto> Events, List<ActivitySessionDto> Sessions)> Chunk(List<ApplicationEventDto> e, List<ActivitySessionDto> s)
    {
        for (int i = 0, j = 0; i < e.Count || j < s.Count; i += AgentLimits.MaxEventsPerBatch, j += AgentLimits.MaxSessionsPerBatch)
            yield return (e.Skip(i).Take(AgentLimits.MaxEventsPerBatch).ToList(), s.Skip(j).Take(AgentLimits.MaxSessionsPerBatch).ToList());
    }
}
