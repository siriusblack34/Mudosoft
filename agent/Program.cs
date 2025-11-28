using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Mudosoft.Agent;
using Mudosoft.Agent.Models;
using Mudosoft.Agent.Services;
using Mudosoft.Agent.Interfaces; 

// 🏆 KRİTİK DÜZELTME: IHostBuilder yapısına geri dönülüyor
var hostBuilder = Host.CreateDefaultBuilder(args)
    .UseWindowsService() // ⬅️ BU SATIR ARTIK HATA VERMEYECEK
    .ConfigureAppConfiguration((hostContext, config) =>
    {
        // Konfigürasyonunuzu burada yüklersiniz (varsayılanlar zaten yüklenir)
    })
    .ConfigureServices((hostContext, services) =>
    {
        // AgentConfig konfigürasyonunu yükle
        services.Configure<AgentConfig>(
            hostContext.Configuration.GetSection("Agent")
        );
        
        // Identity provider (Kalıcı ID sağlar)
        services.AddSingleton<IDeviceIdentityProvider, DeviceIdentityProvider>(); 
        
        // Worker + services
        services.AddHostedService<AgentWorker>();
        services.AddSingleton<ICommandExecutor, CommandExecutor>();
        services.AddSingleton<ICommandPoller, CommandPoller>(); 
        services.AddSingleton<IHeartbeatSender, HeartbeatService>(); 
        services.AddSingleton<IWatchdogManager, WatchdogManager>();
        services.AddSingleton<ISystemInfoService, SystemInfoService>(); 
        services.AddSingleton<IRsaKeyService, RsaKeyService>(); 
        services.AddSingleton<IAesEncryptionService, AesEncryptionService>();

        // HttpClient
        services.AddHttpClient();
    });

var host = hostBuilder.Build();
host.Run();