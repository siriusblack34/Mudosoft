using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mudosoft.Agent;
using Mudosoft.Agent.Options;
using Mudosoft.Agent.Services;

IHostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Windows service olarak çalışmak için:
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Mudosoft Agent";
});

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));

builder.Services.AddHttpClient("BackendClient", (sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// DI kayıtları
builder.Services.AddSingleton<ISystemInfoCollector, SystemInfoCollector>();
builder.Services.AddSingleton<IHeartbeatSender, HeartbeatSender>();
builder.Services.AddSingleton<ICommandExecutor, CommandExecutor>();
builder.Services.AddSingleton<IEventPublisher, EventPublisher>();
builder.Services.AddSingleton<IWatchdogManager, WatchdogManager>();
builder.Services.AddSingleton<ICommandPoller, CommandPoller>();

builder.Services.AddHostedService<AgentWorker>();

IHost host = builder.Build();
await host.RunAsync();
