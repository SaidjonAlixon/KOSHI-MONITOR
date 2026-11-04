using KOSHI.Agent.Core;
using KOSHI.Agent.Service;
using Serilog;

if (args.Length > 0 && args[0] is "activate" or "deactivate")
    return await Cli.RunAsync(args);

Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
    .WriteTo.File(Path.Combine(AgentPaths.MachineLogDir, "service-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "KOSHIAgentService");
builder.Services.AddSerilog();
builder.Services.AddSingleton<PolicyState>();
builder.Services.AddHostedService<PolicyEnforcementWorker>();
builder.Services.AddHostedService<InstallGuard>();
builder.Services.AddHostedService<UninstallGuard>();
builder.Services.AddHostedService<SessionAgentWatchdog>();
builder.Services.AddHostedService<AgentUpdateWorker>();

var host = builder.Build();
await host.RunAsync();
return 0;
