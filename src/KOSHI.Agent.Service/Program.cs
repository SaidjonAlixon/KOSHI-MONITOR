using KOSHI.Agent.Core;
using KOSHI.Agent.Service;
using Serilog;

if (args.Length > 0 && args[0] is "activate" or "deactivate" or "heal")
    return await Cli.RunAsync(args);

Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
    .WriteTo.File(Path.Combine(AgentPaths.MachineLogDir, "service-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

// A fresh service process means any in-progress upgrade finished: clear the stand-down lock so
// self-heal and protections resume and the scheduled tasks are re-enabled.
try { SelfHeal.ClearUpgradeLock(); } catch { /* best-effort */ }

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
