using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Agent;
using Orchestra.Agent.Models;
using Orchestra.Agent.Services;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Services.Collectors;
using System.Diagnostics;
using System.Security.Principal;
using System.Linq; 
using System; 
using System.Runtime.Versioning; 
using Microsoft.Extensions.Configuration; 
using Microsoft.Extensions.FileProviders; 
using System.Reflection; 

// Bu proje bir Windows Servisi olduğu için tüm CA1416 (Platform uyumluluğu) uyarılarını bastırır.
[assembly: SupportedOSPlatform("windows")]

// ==================== EARLIEST POSSIBLE CRASH DETECTION ====================
// Log dosyası: exe dizini > C:\ root > TEMP dizini sırasıyla denenir
string _helperLogPath = @"C:\mudosoft_helper.log"; // default
try
{
    var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
    if (!string.IsNullOrEmpty(exeDir))
        _helperLogPath = System.IO.Path.Combine(exeDir, "mudosoft_helper.log");
}
catch { }

void HelperLog(string msg)
{
    var line = $"{DateTime.Now}: {msg}{Environment.NewLine}";
    // Birden fazla konuma yazmayı dene
    try { System.IO.File.AppendAllText(_helperLogPath, line); } catch { }
    try { System.IO.File.AppendAllText(@"C:\mudosoft_helper.log", line); } catch { }
    
    // Public Debug Log - Herkes yazabilsin
    try { 
        var publicLog = @"C:\Users\Public\mudosoft_helper_debug.log";
        System.IO.File.AppendAllText(publicLog, line); 
    } catch { }

    // Kullanıcının %TEMP% klasörüne de yaz (en garantisi)
    try { 
        var tempLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mudosoft_helper_user.log");
        System.IO.File.AppendAllText(tempLog, line); 
    } catch { }
}

AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    HelperLog($"❗ UNHANDLED EXCEPTION: {e.ExceptionObject}");
};

HelperLog($"[BOOT] CLR started. ENV: PID={Environment.ProcessId}, CWD={Environment.CurrentDirectory}, User={Environment.UserName}, Interactive={Environment.UserInteractive}, ExePath={Environment.ProcessPath}");
HelperLog("BUILD_VERIFICATION: FIX_APPLIED_v9_1_SELF_UPDATE");

// 🔍 HELPER CRASH DEBUG - En başta log
HelperLog($"Program started. Args: {string.Join(" ", args)}.");

try {
HelperLog("[BOOT] entering hostBuilder configuration");
// ========== NORMAL/SERVICE MODE: Full Host ==========
var hostBuilder = Host.CreateDefaultBuilder(args)
    .UseWindowsService() 
    .ConfigureAppConfiguration((hostContext, config) =>
    {
        // Fiziksel dosyalardan yapılandırma yükle (exe'nin yanındaki dosyalar)
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var environmentName = hostContext.HostingEnvironment.EnvironmentName;

        config.SetBasePath(basePath);
        
        // appsettings.json yükle
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        // appsettings.{Environment}.json yükle
        config.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false);
        
        // Ortam değişkenleri ve komut satırı argümanlarını ekle
        config.AddEnvironmentVariables();
        if (args is { Length: > 0 })
        {
            config.AddCommandLine(args);
        }
    })
    .ConfigureServices((hostContext, services) =>
    {
        // Mode Detection:
        // --service = Manager (service that launches helper)
        bool isService = args.Contains("--service");

        // Common Services
        services.Configure<AgentConfig>(hostContext.Configuration.GetSection("Agent"));
        services.AddSingleton<IDeviceIdentityProvider, DeviceIdentityProvider>();
        services.AddHttpClient();

        // --- NORMAL MODE (Console or Service) ---
        // Tüm yönetim servisleri
        services.AddHostedService<AgentWorker>();
        services.AddSingleton<VncInstallerService>();
        services.AddSingleton<ICommandExecutor, CommandExecutor>();
        services.AddSingleton<ICommandPoller, CommandPoller>(); 
        
        // HeartbeatService - hem IHeartbeatSender hem de concrete type olarak kaydet (tray için)
        services.AddSingleton<HeartbeatService>();
        services.AddSingleton<IHeartbeatSender>(sp => sp.GetRequiredService<HeartbeatService>());
        
        services.AddSingleton<IWatchdogManager, WatchdogManager>();
        services.AddSingleton<ISystemInfoService, SystemInfoService>(); 
        services.AddSingleton<IRsaKeyService, RsaKeyService>(); 
        services.AddSingleton<IAesEncryptionService, AesEncryptionService>();
        
        // Tray Communication - Named Pipe Server
        services.AddHostedService<PipeServer>();

        // Periyodik kontrol — backend'in yayinladigi yeni surumu kendiliginden indir + uygula.
        services.AddHostedService<AgentUpdateCheckerService>();

        // Emergency hotfix:
        // Telemetry reconnect + debug log spam was causing unnecessary load on store PCs.
        // Keep the type in the codebase, but do not start it in the hotfix build.
        // services.AddHostedService<TelemetryService>();

        // 9. Collector System
        services.Configure<CollectorsConfig>(hostContext.Configuration.GetSection("Agent:Collectors"));
        services.AddSingleton<CollectorReportSender>();
        services.AddSingleton<ICollector, PortMonitorCollector>();
        services.AddSingleton<ICollector, ProcessUsageCollector>();
        services.AddSingleton<ICollector, ServiceMonitorCollector>();
        services.AddSingleton<ICollector, EventLogCollector>();
        services.AddSingleton<ICollector, DiskHealthCollector>();
        services.AddSingleton<ICollector, WindowsUpdateCollector>();
        services.AddSingleton<ICollector, TemperatureCollector>();
        services.AddSingleton<ICollector, UpsStatusCollector>();
        services.AddSingleton<ICollector, NetworkSpeedCollector>();
        services.AddSingleton<ICollector, UptimeReportCollector>();
        services.AddSingleton<ICollector, ScheduledCleanupCollector>();
        // Emergency hotfix:
        // Suspend non-essential collectors until we re-enable them one by one safely.
        // services.AddHostedService<CollectorOrchestrator>();
    });

HelperLog("[BOOT] hostBuilder configured, calling Build()");
var host = hostBuilder.Build();
HelperLog("[BOOT] host built, calling Run() — SCM RUNNING signal will fire after StartAsync returns");
host.Run();
HelperLog("[BOOT] host.Run() returned (service stopping)");

} // end global try
catch (Exception ex)
{
    HelperLog($"CRASH: {ex}");
    throw;
}

return 0;