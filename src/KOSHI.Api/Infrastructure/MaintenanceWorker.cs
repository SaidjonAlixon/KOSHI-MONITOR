using KOSHI.Application.Services;

namespace KOSHI.Api.Infrastructure;

/// <summary>Periodic offline-device detection and data retention. Disable with Maintenance:Enabled=false.</summary>
public sealed class MaintenanceWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<MaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Maintenance:Enabled", true)) return;
        var offlineEvery = TimeSpan.FromMinutes(config.GetValue("Maintenance:OfflineCheckMinutes", 5));
        var retentionEvery = TimeSpan.FromHours(config.GetValue("Maintenance:RetentionHours", 6));
        var lastRetention = DateTime.MinValue;

        using var timer = new PeriodicTimer(offlineEvery);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<MaintenanceService>();
                await RoleService.ReloadCacheAsync(scope.ServiceProvider.GetRequiredService<KOSHI.Application.Abstractions.IKoshiDbContext>(),
                    scope.ServiceProvider.GetRequiredService<KOSHI.Application.Security.RolePermissionCache>(), stoppingToken);
                await svc.DetectOfflineDevicesAsync(stoppingToken);
                if (DateTime.UtcNow - lastRetention >= retentionEvery)
                {
                    await svc.ApplyRetentionAsync(stoppingToken);
                    lastRetention = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Maintenance cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
