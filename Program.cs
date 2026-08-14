using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ortho32Host.Workers;
using Ortho32Host.IPC;
using Ortho32Host.Router;
using Ortho32Host.EventBus;
using Ortho32Host.Services;
using Ortho32Host.Integration;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => { options.ServiceName = "ORTHOHost"; });

builder.Services.AddSingleton<ORTHOEventBus>();
builder.Services.AddSingleton<CapabilityBroker>();
builder.Services.AddSingleton<AppRegistry>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<InferenceService>();
builder.Services.AddSingleton<BuildService>();
builder.Services.AddSingleton<DeviceService>();
builder.Services.AddSingleton<ServiceRegistry>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ServiceRegistry>>();
    var reg = new ServiceRegistry(logger);
    reg.Register(sp.GetRequiredService<SessionService>());
    reg.Register(sp.GetRequiredService<InferenceService>());
    reg.Register(sp.GetRequiredService<BuildService>());
    reg.Register(sp.GetRequiredService<DeviceService>());
    reg.Register(sp.GetRequiredService<AppRegistry>());
    return reg;
});
builder.Services.AddSingleton<ORTHORouter>();
builder.Services.AddSingleton<NamedPipeServer>();
builder.Services.AddHostedService<ORTHOHostWorker>();

var host = builder.Build();

if (args.Contains("--integration-test"))
{
    await IntegrationTest.RunAsync(host);
    return;
}

await host.RunAsync();
