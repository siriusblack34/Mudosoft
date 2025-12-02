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


// 2. ARGÜMAN KONTROLÜ VE SERVİS YÖNETİMİ BAŞLANGICI
var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

// 🏆 KRİTİK DÜZELTME: Eğer argüman yoksa VEYA /Install isteniyorsa kurulumu başlat.
if (cliArgs.Any(a => a.Equals("/Install", StringComparison.OrdinalIgnoreCase)) || !cliArgs.Any())
{
    // Kurulum isteniyor veya Çift Tıklama algılandı.
    if (!IsAdministrator())
    {
        Console.WriteLine("Kurulum için yönetici yetkisi gerekiyor. Yeniden başlatılıyor...");
        RelaunchWithElevation("/Install");
        return 0; 
    }

    Console.WriteLine($"Windows Servisi kuruluyor: {DisplayName}...");
    
    string binPathArg = $"binPath= \"{Environment.ProcessPath} --service\"";
    
    RunServiceCommand("create", ServiceName, $"start= auto {binPathArg}");
    RunServiceCommand("description", ServiceName, $"\"{ServiceDescription}\"");
    
    Console.WriteLine($"Servis başlatılıyor: {ServiceName}...");
    RunServiceCommand("start", ServiceName);

    Console.WriteLine("Kurulum ve başlatma tamamlandı. Pencereyi kapatabilirsiniz.");
    Console.ReadKey();
    return 0;
}

if (cliArgs.Any(a => a.Equals("/Uninstall", StringComparison.OrdinalIgnoreCase)))
{
    // Kaldırma isteniyor
    if (!IsAdministrator())
    {
        Console.WriteLine("Kaldırma için yönetici yetkisi gerekiyor. Yeniden başlatılıyor...");
        RelaunchWithElevation("/Uninstall");
        return 0; 
    }

    Console.WriteLine($"Windows Servisi durduruluyor ve kaldırılıyor: {ServiceName}...");
    RunServiceCommand("stop", ServiceName);
    RunServiceCommand("delete", ServiceName);
    
    Console.WriteLine("Kaldırma işlemi tamamlandı. Pencereyi kapatabilirsiniz.");
    Console.ReadKey();
    return 0;
}


// 3. NORMAL ÇALIŞMA VEYA WINDOWS SERVİSİ OLARAK ÇALIŞMA (Varsayılan Akış)

var hostBuilder = Host.CreateDefaultBuilder(args)
    .UseWindowsService() 
    .ConfigureAppConfiguration((hostContext, config) =>
    {
        // Yapılandırmayı gömülü kaynaklardan yükle
        config.Sources.Clear(); 

        var assembly = Assembly.GetExecutingAssembly();
        var environmentName = hostContext.HostingEnvironment.EnvironmentName;

        var embeddedProvider = new EmbeddedFileProvider(assembly);

        // appsettings.json yükle
        config.AddJsonFile(embeddedProvider, "appsettings.json", optional: false, reloadOnChange: false);

        // appsettings.{Environment}.json yükle
        config.AddJsonFile(embeddedProvider, $"appsettings.{environmentName}.json", optional: true, reloadOnChange: false);
        
        // Ortam değişkenleri ve komut satırı argümanlarını ekle (varsayılan host builder davranışı)
        config.AddEnvironmentVariables();
        if (args is { Length: > 0 })
        {
            config.AddCommandLine(args);
        }
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

return 0;