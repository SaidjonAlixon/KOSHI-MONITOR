using System.Text.Json;
using KOSHI.Agent.Core;
using KOSHI.Contracts;

namespace KOSHI.Agent.Service;

/// <summary>
/// Latest effective policy shared by the service workers. Cached on disk so web, VPN and installation
/// restrictions keep working after a reboot while the server is unreachable.
/// </summary>
public sealed class PolicyState
{
    private static readonly string CachePath = Path.Combine(AgentPaths.MachineDir, "service-policy.json");
    private readonly SemaphoreSlim _refresh = new(0, 1);
    private AgentPolicyDto? _current;

    public AgentPolicyDto? Current => Volatile.Read(ref _current);

    public void Set(AgentPolicyDto policy)
    {
        Volatile.Write(ref _current, policy);
        try
        {
            Directory.CreateDirectory(AgentPaths.MachineDir);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(policy, KoshiApiClient.Json));
        }
        catch (IOException)
        {
            // Cache is best-effort.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void LoadCache()
    {
        if (Current is not null || !File.Exists(CachePath)) return;
        try
        {
            var cached = JsonSerializer.Deserialize<AgentPolicyDto>(File.ReadAllText(CachePath), KoshiApiClient.Json);
            if (cached is not null) Volatile.Write(ref _current, cached);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Wakes the policy loop now (the tray forwards the server's "refresh-policy" push).</summary>
    public void RequestRefresh()
    {
        try { _refresh.Release(); }
        catch (SemaphoreFullException) { }
    }

    public async Task WaitForRefreshAsync(TimeSpan timeout, CancellationToken ct) => await _refresh.WaitAsync(timeout, ct);
}
