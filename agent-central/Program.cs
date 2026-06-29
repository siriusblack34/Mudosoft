using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orchestra.CentralAgent.Services;
using System.Windows.Forms;

// ─── İlk çalıştırmada otomatik kurulum ─────────────────────────────────────

var cmdArgs = Environment.GetCommandLineArgs();

if (cmdArgs.Contains("--service"))
{
    var host = Host.CreateDefaultBuilder(cmdArgs)
        .UseWindowsService(opts => opts.ServiceName = "OrchestraCentralAgent")
        .ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddEmbeddedJsonFile("appsettings.json");
        })
        .ConfigureServices((ctx, services) =>
        {
            services.Configure<CentralAgentConfig>(ctx.Configuration.GetSection("CentralAgent"));
            services.AddSingleton<DeviceIdentityService>();
            services.AddSingleton<VncSetupService>();
            services.AddHostedService<HeartbeatService>();
            // CentralDesktopService (UI) kullanıcı oturumunda çalışır,
            // servis (Session 0) bunu HelperLauncherService ile spawn eder.
            services.AddHostedService<HelperLauncherService>();
        })
        .Build();

    await host.RunAsync();
    return;
}

if (cmdArgs.Contains("--helper"))
{
    void HLog(string m) { try { System.IO.File.AppendAllText(@"C:\ProgramData\OrchestraCentralAgent\helper.log", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [start] {m}{Environment.NewLine}"); } catch { } }
    HLog($"--helper basladi (sessionId={Environment.ProcessId})");

    // Tek örnek (oturum bazlı) — birden fazla başlatıcı (servis + scheduled task) olsa bile
    // her kullanıcı oturumunda yalnızca BİR helper çalışsın; çift onay diyaloğunu engeller.
    using var helperMutex = new System.Threading.Mutex(true, @"Local\OrchestraCentralAgentHelper", out bool createdNew);
    HLog($"mutex createdNew={createdNew}");
    if (!createdNew)
    {
        HLog("zaten helper var, cikiliyor");
        return; // Bu oturumda zaten bir helper çalışıyor
    }

    try
    {
    var host = Host.CreateDefaultBuilder(cmdArgs)
        .ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddEmbeddedJsonFile("appsettings.json");
        })
        .ConfigureServices((ctx, services) =>
        {
            services.Configure<CentralAgentConfig>(ctx.Configuration.GetSection("CentralAgent"));
            services.AddSingleton<DeviceIdentityService>();
            services.AddHostedService<CentralDesktopService>();
        })
        .Build();

    HLog("host kuruldu, RunAsync cagriliyor");
    await host.RunAsync();
    HLog("RunAsync dondu (host durdu)");
    }
    catch (Exception ex)
    {
        HLog($"HATA: {ex}");
    }
    return;
}

// ─── İlk çalıştırma / Kurulum modu ─────────────────────────────────────────
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var config = new ConfigurationBuilder()
    .AddEmbeddedJsonFile("appsettings.json")
    .Build();

var agentConfig = config.GetSection("CentralAgent").Get<CentralAgentConfig>()
    ?? new CentralAgentConfig();

await InstallerService.RunAsync(agentConfig);
