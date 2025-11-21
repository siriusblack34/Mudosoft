// agent/Program.cs
using Microsoft.Extensions.Hosting;
using Mudosoft.Agent;
using Mudosoft.Agent.Models;
using Mudosoft.Agent.Services;

// Eklendi: Arayüzlerin (Interfaces) bulunduğu namespace
using Mudosoft.Agent.Interfaces; 


var builder = Host.CreateApplicationBuilder(args);

// "Agent" section → AgentConfig
builder.Services.Configure<AgentConfig>(
    builder.Configuration.GetSection("Agent")
);

// Identity provider (istersen ileride kullanırsın)
builder.Services.AddSingleton<DeviceIdentityProvider>();

// Worker + services
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddSingleton<ICommandExecutor, CommandExecutor>();
builder.Services.AddSingleton<ICommandPoller, CommandPoller>();
builder.Services.AddSingleton<IHeartbeatSender, HeartbeatService>();
builder.Services.AddSingleton<IWatchdogManager, WatchdogManager>();
// Artık ISystemInfoService'i Interfaces'ten ve SystemInfoService'i Services'ten biliyoruz.
builder.Services.AddSingleton<ISystemInfoService, SystemInfoService>(); 
builder.Services.AddSingleton<IRsaKeyService, RsaKeyService>(); 
builder.Services.AddSingleton<IAesEncryptionService, AesEncryptionService>();


// HttpClient
builder.Services.AddHttpClient();

// Build & run
var host = builder.Build();
host.Run();