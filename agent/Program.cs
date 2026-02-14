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
System.IO.File.AppendAllText(@"C:\mudosoft_helper.log", $"{DateTime.Now}: Program started. Args: {string.Join(" ", args)}. isHelperMode: {isHelperMode}{Environment.NewLine}");

if (isHelperMode)
{
    try
    {
        // Hemen heartbeat yaz - manager'a hayatta olduğumuzu bildir
        System.IO.File.WriteAllText(@"C:\MudoSoftAgent\helper_running.flag", DateTime.Now.ToString("o"));
        System.IO.File.AppendAllText(@"C:\mudosoft_helper.log", $"{DateTime.Now}: Heartbeat written immediately in Program.cs{Environment.NewLine}");
    }
    catch (Exception ex)
    {
        System.IO.File.AppendAllText(@"C:\mudosoft_helper.log", $"{DateTime.Now}: ERROR writing heartbeat: {ex.Message}{Environment.NewLine}");
    }
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


// 1.5. ESKİ/ÇAKIŞAN TÜM MUDOSOFT BİLEŞENLERİNİ TEMİZLEME
static void CleanAllMudoSoftComponents()
{
    Console.WriteLine("========================================");
    Console.WriteLine("🧹 Eski MudoSoft bileşenleri temizleniyor...");
    Console.WriteLine("========================================");
    
    // 1. Çalışan process'leri sonlandır
    Console.WriteLine("\n[1/6] Çalışan process'ler sonlandırılıyor...");
    try
    {
        foreach (var proc in Process.GetProcessesByName("MudoSoft.Agent"))
        {
            try { proc.Kill(); proc.WaitForExit(3000); Console.WriteLine("  ✓ MudoSoft.Agent.exe sonlandırıldı"); }
            catch { }
        }
        foreach (var proc in Process.GetProcessesByName("MudoSoft.Tray"))
        {
            try { proc.Kill(); proc.WaitForExit(3000); Console.WriteLine("  ✓ MudoSoft.Tray.exe sonlandırıldı"); }
            catch { }
        }
    }
    catch { Console.WriteLine("  ⚠ Process sonlandırma hatası (yoksayıldı)"); }
    
    // 2. Servisleri kaldır
    Console.WriteLine("\n[2/6] Servisler kaldırılıyor...");
    string[] serviceNames = { "MudoSoftAgent", "MudosoftAgentService", "MudoSoftAgentService" };
    foreach (var name in serviceNames)
    {
        try
        {
            RunServiceCommand("stop", name);
            RunServiceCommand("delete", name);
        }
        catch { }
    }
    
    // 3. Scheduled Task'ları kaldır
    Console.WriteLine("\n[3/6] Scheduled Task'lar kaldırılıyor...");
    string[] taskNames = { "MudoSoftHelper", "MudosoftHelper", "MudoSoft Helper" };
    foreach (var taskName in taskNames)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo.FileName = "schtasks";
            proc.StartInfo.Arguments = $"/delete /tn \"{taskName}\" /f";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.Start();
            proc.WaitForExit(5000);
            if (proc.ExitCode == 0) Console.WriteLine($"  ✓ Task kaldırıldı: {taskName}");
        }
        catch { }
    }
    
    // 4. Registry temizliği (Startup entries)
    Console.WriteLine("\n[4/6] Registry kayıtları temizleniyor...");
    string[] registryKeys = {
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    };
    string[] valueNames = { "MudoSoftHelper", "MudosoftHelper", "MudoSoft.Tray", "MudoSoftTray" };
    
    foreach (var key in registryKeys)
    {
        foreach (var valueName in valueNames)
        {
            try
            {
                using var proc = new Process();
                proc.StartInfo.FileName = "reg";
                proc.StartInfo.Arguments = $"delete \"{key}\" /v \"{valueName}\" /f";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.CreateNoWindow = true;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.Start();
                proc.WaitForExit(3000);
                if (proc.ExitCode == 0) Console.WriteLine($"  ✓ Registry silindi: {valueName}");
            }
            catch { }
        }
    }
    
    // 5. Eski klasörleri tara ve listele (silmeyiz, kullanıcıya bırakırız)
    Console.WriteLine("\n[5/6] Eski kurulum klasörleri kontrol ediliyor...");
    string[] possiblePaths = {
        @"C:\MudoSoft",
        @"C:\MudoSoft-Agent-v1.0.0.15",
        @"C:\MudoSoft-Agent-v1.0.0.16",
        @"C:\MudoSoft-Agent-v1.0.0.17",
        @"C:\MudoSoft-Agent-v1.0.0.18",
        @"C:\Program Files\MudoSoft",
        @"C:\Program Files (x86)\MudoSoft"
    };
    
    foreach (var path in possiblePaths)
    {
        if (Directory.Exists(path))
        {
            Console.WriteLine($"  ⚠ Eski klasör bulundu: {path}");
        }
    }
    
    // 6. VBS dosyalarını temizle
    Console.WriteLine("\n[6/6] Helper script dosyaları temizleniyor...");
    foreach (var path in possiblePaths)
    {
        string vbsPath = Path.Combine(path, "start-helper.vbs");
        if (File.Exists(vbsPath))
        {
            try { File.Delete(vbsPath); Console.WriteLine($"  ✓ Silindi: {vbsPath}"); }
            catch { Console.WriteLine($"  ⚠ Silinemedi: {vbsPath}"); }
        }
    }
    
    Console.WriteLine("\n✅ Temizlik tamamlandı!");
    Console.WriteLine("========================================\n");
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

    // Önce tüm eski bileşenleri temizle
    CleanAllMudoSoftComponents();

    Console.WriteLine($"Windows Servisi kuruluyor: {DisplayName}...");
    
    string binPathArg = $"binPath= \"{Environment.ProcessPath} --service\"";
    string installDir = AppDomain.CurrentDomain.BaseDirectory;
    string exePath = Environment.ProcessPath ?? Path.Combine(installDir, "MudoSoft.Agent.exe");
    
    // type= interact type= own → Local System'ın desktop ile etkileşmesine izin verir
    RunServiceCommand("create", ServiceName, $"start= auto type= interact type= own {binPathArg}");
    RunServiceCommand("description", ServiceName, $"\"{ServiceDescription}\"");
    
    Console.WriteLine($"Servis başlatılıyor: {ServiceName}...");
    RunServiceCommand("start", ServiceName);

    // ========== HELPER AUTO-START KURULUMU ==========
    Console.WriteLine("");
    Console.WriteLine("Desktop Helper için otomatik başlatma ayarlanıyor...");
    
    // 1. VBS Helper Script oluştur (gizli pencere ile çalıştırma için)
    // NOT: VBS'de tırnak escape: "" = tek tırnak, path'de 3 tırnak başta, 2 tırnak sonda
    string vbsPath = Path.Combine(installDir, "start-helper.vbs");
    string vbsLine1 = "Set WshShell = CreateObject(\"WScript.Shell\")";
    string vbsLine2 = $"WshShell.Run \"\"\"{exePath}\"\" --desktop-helper\", 0, False";
    string vbsContent = vbsLine1 + Environment.NewLine + vbsLine2;
    
    try
    {
        File.WriteAllText(vbsPath, vbsContent);
        Console.WriteLine($"✓ Helper script oluşturuldu: {vbsPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠ Helper script oluşturulamadı: {ex.Message}");
    }

    // 2. Registry ile otomatik başlatma (Scheduled Task yerine - Windows 7 uyumlu)
    Console.WriteLine("Registry startup kayıtları ekleniyor...");
    string wscriptCmd = $"wscript.exe \"{vbsPath}\"";
    
    // HKCU - mevcut kullanıcı için (her zaman çalışır)
    try
    {
        using var regProc = new Process();
        regProc.StartInfo.FileName = "reg";
        regProc.StartInfo.Arguments = $"add \"HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"MudoSoftHelper\" /t REG_SZ /d \"{wscriptCmd}\" /f";
        regProc.StartInfo.UseShellExecute = false;
        regProc.StartInfo.CreateNoWindow = true;
        regProc.StartInfo.RedirectStandardOutput = true;
        regProc.Start();
        regProc.WaitForExit();
        if (regProc.ExitCode == 0)
            Console.WriteLine("✓ Registry startup eklendi (HKCU)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠ Registry hatası: {ex.Message}");
    }

    // 3. Helper'ı hemen şimdi başlat (kullanıcı tekrar login olmak zorunda kalmasın)
    Console.WriteLine("");
    Console.WriteLine("Desktop Helper şimdi başlatılıyor...");
    try
    {
        var helperProc = new Process();
        helperProc.StartInfo.FileName = "wscript.exe";
        helperProc.StartInfo.Arguments = $"\"{vbsPath}\"";
        helperProc.StartInfo.UseShellExecute = false;
        helperProc.StartInfo.CreateNoWindow = true;
        helperProc.Start();
        Console.WriteLine("✓ Desktop Helper başlatıldı");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠ Helper başlatılamadı: {ex.Message}");
        Console.WriteLine($"  Manuel başlatmak için: {exePath} --desktop-helper");
    }

    Console.WriteLine("");
    Console.WriteLine("========================================");
    Console.WriteLine("✅ KURULUM TAMAMLANDI!");
    Console.WriteLine("========================================");
    Console.WriteLine("");
    Console.WriteLine("Kurulan bileşenler:");
    Console.WriteLine("  ✓ Windows Servisi: MudosoftAgentService (Arka plan yönetimi)");
    Console.WriteLine("  ✓ Desktop Helper: Otomatik başlatma (Registry startup)");
    Console.WriteLine("  ✓ Remote Desktop: Hazır");
    Console.WriteLine("");
    Console.WriteLine("Kurulum klasörü: " + installDir);
    Console.WriteLine("========================================");
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

    // Tüm MudoSoft bileşenlerini temizle
    CleanAllMudoSoftComponents();
    
    Console.WriteLine("");
    Console.WriteLine("========================================");
    Console.WriteLine("✅ KALDIRMA TAMAMLANDI!");
    Console.WriteLine("========================================");
    Console.WriteLine("");
    Console.WriteLine("Kalan dosyaları manuel olarak silebilirsiniz:");
    Console.WriteLine("  C:\\MudoSoft klasörü");
    Console.WriteLine("========================================");
    return 0;
}


// 3. NORMAL ÇALIŞMA VEYA WINDOWS SERVİSİ OLARAK ÇALIŞMA (Varsayılan Akış)

// ========== HELPER MODE: Minimal Host (WMI-free) ==========
if (isHelperMode)
{
    System.IO.File.AppendAllText(@"C:\mudosoft_helper.log", $"{DateTime.Now}: Starting minimal helper host{Environment.NewLine}");
    
    var helperHost = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((hostContext, config) =>
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            config.SetBasePath(basePath);
            config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        })
        .ConfigureServices((hostContext, services) =>
        {
            // ONLY minimal services for helper - NO WMI dependencies
            services.Configure<AgentConfig>(hostContext.Configuration.GetSection("Agent"));
            services.AddSingleton<IDeviceIdentityProvider, DeviceIdentityProvider>();
            services.AddSingleton(new RemoteDesktopConfig { Mode = "Helper" });
            services.AddHostedService<RemoteDesktopService>();
        })
        .Build();
    
    System.IO.File.AppendAllText(@"C:\mudosoft_helper.log", $"{DateTime.Now}: Helper host built, running...{Environment.NewLine}");
    helperHost.Run();
    return 0;
}

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
        
        // Pass Mode to Configuration
        services.AddSingleton(new RemoteDesktopConfig { Mode = "Manager" });

        // --- NORMAL MODE (Console or Service) ---
        // Tüm yönetim servisleri
        services.AddHostedService<AgentWorker>();
        services.AddSingleton<ICommandExecutor, CommandExecutor>();
        services.AddSingleton<ICommandPoller, CommandPoller>(); 
        
        // HeartbeatService - hem IHeartbeatSender hem de concrete type olarak kaydet (tray için)
        services.AddSingleton<HeartbeatService>();
        services.AddSingleton<IHeartbeatSender>(sp => sp.GetRequiredService<HeartbeatService>());
        
        services.AddSingleton<IWatchdogManager, WatchdogManager>();
        services.AddSingleton<ISystemInfoService, SystemInfoService>(); 
        services.AddSingleton<IRsaKeyService, RsaKeyService>(); 
        services.AddSingleton<IAesEncryptionService, AesEncryptionService>();
        
        // HelperLauncher for elevated Remote Desktop (runs as BackgroundService)
        services.AddHostedService<HelperLauncher>();
        
        // Tray Communication - Named Pipe Server
        services.AddHostedService<PipeServer>();

        // RemoteDesktopService - Mode'a göre stream veya launch
        services.AddHostedService<RemoteDesktopService>();
        
        // 8. Telemetry Service (Real-time Dashboard)
        services.AddHostedService<TelemetryService>();
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