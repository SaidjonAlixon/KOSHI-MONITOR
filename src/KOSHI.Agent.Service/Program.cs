var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "KOSHI Agent Service");

var host = builder.Build();
host.Run();
