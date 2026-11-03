using KOSHI.Agent.Core;
using KOSHI.Contracts;

namespace KOSHI.Agent.Service;

/// <summary>Fetches the effective policy and applies admin-only mechanisms (AppLocker), reporting every result to the server.</summary>
public sealed class PolicyEnforcementWorker(ILogger<PolicyEnforcementWorker> logger) : BackgroundService
{
    private (Guid? Id, int? Version, PolicyModeDto Mode, bool Approved)? _lastApplied;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (DeviceRejectedException ex)
            {
                logger.LogWarning("Device rejected by server: {Reason}", ex.Reason);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Server unreachable; will retry");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Policy enforcement cycle failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var config = AgentStore.LoadConfig();
        var credential = AgentStore.LoadCredential();
        if (config is null || credential is null) return;

        using var api = new KoshiApiClient(config.ServerUrl, credential);
        var policy = await api.GetPolicyAsync(ct);
        var key = (policy.PolicyId, policy.Version, policy.Mode, policy.EnforcementApproved);
        if (_lastApplied == key) return;

        var result = AppLockerAdapter.Apply(policy);
        logger.LogInformation("Policy v{Version} via {Adapter}: {Status}", policy.Version, result.Adapter, result.Status);
        if (policy.PolicyId is { } id && policy.Version is { } version)
        {
            await api.ReportPolicyResultAsync(new PolicyResultReportRequest
            {
                PolicyId = id, PolicyVersion = version, Adapter = result.Adapter, Mode = policy.Mode, Status = result.Status, Message = result.Message,
            }, ct);
        }
        _lastApplied = key;
    }
}
