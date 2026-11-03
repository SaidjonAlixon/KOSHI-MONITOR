using KOSHI.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KOSHI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddKoshiApplication(this IServiceCollection services, Action<AuthOptions>? configureAuth = null)
    {
        var auth = new AuthOptions();
        configureAuth?.Invoke(auth);
        services.AddSingleton(auth);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Security.RolePermissionCache>();
        services.AddScoped<RoleService>();

        services.AddScoped<AuditWriter>();
        services.AddScoped<SettingsService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<PolicyService>();
        services.AddScoped<DeviceService>();
        services.AddScoped<TelemetryService>();
        services.AddScoped<ReportService>();
        services.AddScoped<MonitoringService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<AlertService>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<MaintenanceService>();
        services.AddScoped<SupportService>();
        return services;
    }
}
