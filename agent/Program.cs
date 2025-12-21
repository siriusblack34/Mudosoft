using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Mudosoft.Agent;
using Mudosoft.Agent.Models;
using Mudosoft.Agent.Services;
using Mudosoft.Agent.Interfaces; 
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

// 🔍 HELPER CRASH DEBUG - En başta log
bool isHelperMode = args.Contains("--desktop-helper");
if (isHelperMode)
{
    try
    {
        System.IO.File.AppendAllText(@"C:\mudosoft_helper.log", $"{DateTime.Now}: Helper started with args: {string.Join(" ", args)}{Environment.NewLine}");
    }
    catch { }
}

try  // Global try-catch for Helper crash detection
{

// 💡 KURULUM/KALDIRMA İÇİN KRİTİK DEĞİŞKENLER
const string ServiceName = "MudosoftAgentService"; 
const string DisplayName = "Mudosoft POS Agent";   
const string ServiceDescription = "Mudosoft Retail POS cihazları için merkezi yönetim ve komut ajanı.";

// 1. KENDİ KENDİNE KURULUM VE YÖNETİM İÇİN YARDIMCI FONKSİYONLAR
static bool IsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}

static void RelaunchWithElevation(string verb)
{
    var startInfo = new ProcessStartInfo
    {
        UseShellExecute = true,
        WorkingDirectory = Environment.CurrentDirectory,
        FileName = Environment.ProcessPath, 
        Verb = "runas",
        Arguments = verb 
    };

    try
    {
        Process.Start(startInfo);
    }
    catch (System.ComponentModel.Win32Exception)
    {
        Console.WriteLine("Yönetici yetkisi reddedildi. Kurulum/Kaldırma yapılamadı.");
    }
}

static int RunServiceCommand(string command, string serviceName, string? arguments = null) 
{
    using var process = new Process();
    
    string scArguments = $"{command} \"{serviceName}\"";
    if (!string.IsNullOrEmpty(arguments))
    {
        scArguments += $" {arguments}";
    }

    process.StartInfo.FileName = "sc";
    process.StartInfo.Arguments = scArguments;
    process.StartInfo.UseShellExecute = false;
    process.StartInfo.RedirectStandardOutput = true;
    process.StartInfo.CreateNoWindow = true; 
    
    process.Start();
    process.WaitForExit();
    
    if (process.ExitCode == 0)
    {
        Console.WriteLine($"İşlem başarılı: {command} {serviceName}");
    }
    else
    {
        Console.WriteLine($"İşlem HATA verdi (Exit Code: {process.ExitCode}): {command} {serviceName}");
    }
    
    return process.ExitCode;
}


// 1.5. ESKİ/ÇAKIŞAN SERVİSLERİ TEMİZLEME
static void CleanLegacyServices()
{
    string[] legacyNames = { "MudoSoftAgent", "MudosoftAgentService" };
    foreach (var name in legacyNames)
    {
        try 
        {
            Console.WriteLine($"Eski servis kontrol ediliyor: {name}...");
            RunServiceCommand("stop", name);
            RunServiceCommand("delete", name);
        }
        catch { /* Yoksay */}
    }
}

// 2. ARGÜMAN KONTROLÜ VE SERVİS YÖNETİMİ
var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (cliArgs.Any(a => a.Equals("/Install", StringComparison.OrdinalIgnoreCase)))
{
    if (!IsAdministrator())
    {
        Console.WriteLine("Kurulum için yönetici yetkisi gerekiyor. Yeniden başlatılıyor...");
        RelaunchWithElevation("/Install");
        return 0; 
    }

    Console.WriteLine("Mevcut/Eski servisler temizleniyor...");
    CleanLegacyServices();

    Console.WriteLine($"Windows Servisi kuruluyor: {DisplayName}...");
    
    string binPathArg = $"binPath= \"{Environment.ProcessPath} --service\"";
    
    // type= interact type= own → Local System'ın desktop ile etkileşmesine izin verir
    RunServiceCommand("create", ServiceName, $"start= auto type= interact type= own {binPathArg}");
    RunServiceCommand("description", ServiceName, $"\"{ServiceDescription}\"");
    
    Console.WriteLine($"Servis başlatılıyor: {ServiceName}...");
    RunServiceCommand("start", ServiceName);

    Console.WriteLine("");
    Console.WriteLine("========================================");
    Console.WriteLine("Kurulum tamamlandı!");
    Console.WriteLine("");
    Console.WriteLine("NOT: Remote Desktop çalışmazsa, servisi bir domain admin");
    Console.WriteLine("hesabıyla çalıştırmanız gerekebilir:");
    Console.WriteLine("  1. services.msc açın");
    Console.WriteLine("  2. MudosoftAgentService → Özellikler → Oturum Aç");
    Console.WriteLine("  3. 'Bu hesap' seçip domain admin bilgilerini girin");
    Console.WriteLine("========================================");
    Console.ReadKey();
    return 0;
}

if (cliArgs.Any(a => a.Equals("/Uninstall", StringComparison.OrdinalIgnoreCase)))
{
    if (!IsAdministrator())
    {
        Console.WriteLine("Kaldırma için yönetici yetkisi gerekiyor. Yeniden başlatılıyor...");
        RelaunchWithElevation("/Uninstall");
        return 0; 
    }

    Console.WriteLine("Servisler kaldırılıyor...");
    CleanLegacyServices();
    
    Console.WriteLine("Kaldırma işlemi tamamlandı. Pencereyi kapatabilirsiniz.");
    Console.ReadKey();
    return 0;
}


// 3. NORMAL ÇALIŞMA VEYA WINDOWS SERVİSİ OLARAK ÇALIŞMA (Varsayılan Akış)

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
        // --desktop-helper = Helper (launched by service)
        // --service = Manager (service that launches helper)
        // (nothing) = Helper (console mode, direct streaming)
        bool isDesktopHelper = args.Contains("--desktop-helper");
        bool isService = args.Contains("--service");
        
        // Konsol modunda veya --desktop-helper ile: Helper (doğrudan streaming)
        // Servis modunda (--service): Manager (helper başlatır)
        bool shouldStream = isDesktopHelper || !isService;

        // Common Services
        services.Configure<AgentConfig>(hostContext.Configuration.GetSection("Agent"));
        services.AddSingleton<IDeviceIdentityProvider, DeviceIdentityProvider>();
        services.AddHttpClient();
        
        // Pass Mode to Configuration
        services.AddSingleton(new RemoteDesktopConfig { Mode = shouldStream ? "Helper" : "Manager" });

        if (isDesktopHelper)
        {
            // --- HELPER MODE (User Session - launched by service) ---
            // Sadece streaming servisini çalıştır
            services.AddHostedService<RemoteDesktopService>();
        }
        else
        {
            // --- NORMAL MODE (Console or Service) ---
            // Tüm yönetim servisleri
            services.AddHostedService<AgentWorker>();
            services.AddSingleton<ICommandExecutor, CommandExecutor>();
            services.AddSingleton<ICommandPoller, CommandPoller>(); 
            services.AddSingleton<IHeartbeatSender, HeartbeatService>(); 
            services.AddSingleton<IWatchdogManager, WatchdogManager>();
            services.AddSingleton<ISystemInfoService, SystemInfoService>(); 
            services.AddSingleton<IRsaKeyService, RsaKeyService>(); 
            services.AddSingleton<IAesEncryptionService, AesEncryptionService>();

            // RemoteDesktopService - Mode'a göre stream veya launch
            services.AddHostedService<RemoteDesktopService>();
        }
    });

var host = hostBuilder.Build();
host.Run();

} // end global try
catch (Exception ex)
{
    if (isHelperMode)
    {
        System.IO.File.AppendAllText(@"C:\mudosoft_helper.log", $"{DateTime.Now}: CRASH: {ex}{Environment.NewLine}");
    }
    throw;
}

return 0;