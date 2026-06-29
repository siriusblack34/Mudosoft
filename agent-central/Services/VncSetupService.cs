using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Orchestra.CentralAgent.Services;

/// <summary>
/// Merkez cihaza TightVNC kurulumu yapar, random şifre üretir, backend'e bildirir.
/// </summary>
[SupportedOSPlatform("windows")]
public class VncSetupService
{
    private readonly CentralAgentConfig _config;
    private const string PasswordFile = @"C:\ProgramData\OrchestraCentralAgent\vnc_password.dat";

    public VncSetupService(IOptions<CentralAgentConfig> config)
    {
        _config = config.Value;
    }

    public bool IsVncInstalled()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("tvnserver");
            _ = sc.Status;
            return true;
        }
        catch { return false; }
    }

    private static readonly string[] TvnServerPaths =
    {
        @"C:\Program Files\TightVNC\tvnserver.exe",
        @"C:\Program Files (x86)\TightVNC\tvnserver.exe"
    };

    private static string? FindTvnServerExe() => TvnServerPaths.FirstOrDefault(File.Exists);

    /// <summary>
    /// TightVNC servisini ('tvnserver') açıkça kaydeder ve başlatır.
    /// MSI silent kurulumu bazı makinelerde servisi register etmiyor; bu garanti eder.
    /// Servis zaten varsa -install zararsızdır.
    /// </summary>
    private static void EnsureTvnServerService()
    {
        var tvn = FindTvnServerExe();
        if (tvn == null) return;

        RunProcess(tvn, "-install -silent");
        System.Threading.Thread.Sleep(1500);
        RunProcess(tvn, "-start");
        // net start fallback (servis kayıtlıysa ama durmuşsa)
        RunProcess("net.exe", "start tvnserver");
    }

    /// <summary>Port 5900 için güvenlik duvarı kuralını (tüm profiller) garanti eder.</summary>
    private static void EnsureFirewallRule()
    {
        RunProcess("netsh.exe", "advfirewall firewall delete rule name=\"Orchestra VNC\"");
        RunProcess("netsh.exe",
            "advfirewall firewall add rule name=\"Orchestra VNC\" " +
            "dir=in action=allow protocol=TCP localport=5900 " +
            "profile=any description=\"Orchestra Merkez Ajan VNC portu\"");
    }

    /// <summary>
    /// TightVNC registry şifresini DPAPI dosyasındaki şifreyle senkronize eder.
    /// Hem 64-bit (SOFTWARE\TightVNC) hem de 32-bit WOW6432Node konumuna yazar.
    /// </summary>
    public bool SyncVncPassword()
    {
        try
        {
            var password = GetOrGeneratePassword();
            var encPwd   = EncryptVncPassword(password);

            bool synced = false;
            foreach (var keyPath in new[] {
                @"SOFTWARE\TightVNC\Server",
                @"SOFTWARE\WOW6432Node\TightVNC\Server"
            })
            {
                using var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
                if (reg != null)
                {
                    reg.SetValue("Password", encPwd, Microsoft.Win32.RegistryValueKind.Binary);
                    reg.SetValue("LoopbackOnly",      0,    Microsoft.Win32.RegistryValueKind.DWord);
                    reg.SetValue("AcceptConnections", 1,    Microsoft.Win32.RegistryValueKind.DWord);
                    synced = true;
                }
            }

            if (synced)
            {
                // Güvenlik duvarı + servis garanti (silent MSI servisi kaydetmemiş olabilir)
                EnsureFirewallRule();
                RunProcess("net.exe", "stop tvnserver");
                System.Threading.Thread.Sleep(1500);
                EnsureTvnServerService();
            }

            return synced;
        }
        catch { return false; }
    }

    public async Task<(bool success, string message)> InstallAsync()
    {
        try
        {
            var msi = Path.Combine(Path.GetTempPath(), "tightvnc_orchestra.msi");

            // Önce temp'e bakılır (önceki indirme), yoksa backend'den çekil
            if (!File.Exists(msi))
            {
                var url = $"{_config.BackendUrl.TrimEnd('/')}/api/agent/central/tightvnc";
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var bytes = await http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(msi, bytes);
            }

            var password = GetOrGeneratePassword();

            // Silent install
            RunProcess("msiexec.exe",
                $"/i \"{msi}\" /quiet /norestart ADDLOCAL=Server SET_USEFIREWALL=1 SET_ACCEPTHTTPCONNECTIONS=0");

            await Task.Delay(3000);

            // Registry — LoopbackOnly=0 kritik (multi-NIC için)
            using var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\TightVNC\Server", writable: true);
            reg?.SetValue("LoopbackOnly",        0,    Microsoft.Win32.RegistryValueKind.DWord);
            reg?.SetValue("RfbPort",             5900, Microsoft.Win32.RegistryValueKind.DWord);
            reg?.SetValue("AcceptConnections",   1,    Microsoft.Win32.RegistryValueKind.DWord);

            // Şifre set et
            var encPwd = EncryptVncPassword(password);
            reg?.SetValue("Password", encPwd, Microsoft.Win32.RegistryValueKind.Binary);

            // Güvenlik duvarı kuralı — tüm profillerde port 5900 aç
            EnsureFirewallRule();

            // TightVNC servisini açıkça kaydet + başlat (MSI silent kurulum bunu atlayabiliyor)
            RunProcess("net.exe", "stop tvnserver");
            await Task.Delay(1000);
            EnsureTvnServerService();

            // Servis gerçekten kalktı mı? 5900 dinleniyor olmalı
            if (!IsVncInstalled())
                return (false, "TightVNC servisi kaydedilemedi (tvnserver). Lütfen tekrar deneyin.");

            return (true, "TightVNC kurulumu tamamlandı");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public string GetOrGeneratePassword()
    {
        if (File.Exists(PasswordFile))
        {
            try
            {
                var enc = File.ReadAllBytes(PasswordFile);
                return System.Text.Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(enc, null, DataProtectionScope.LocalMachine));
            }
            catch { }
        }

        var password = GenerateRandomPassword(8);
        var encrypted = ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(password), null, DataProtectionScope.LocalMachine);

        Directory.CreateDirectory(Path.GetDirectoryName(PasswordFile)!);
        File.WriteAllBytes(PasswordFile, encrypted);
        return password;
    }

    private static string GenerateRandomPassword(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        using var rng = RandomNumberGenerator.Create();
        var buf = new byte[length];
        rng.GetBytes(buf);
        return new string(buf.Select(b => chars[b % chars.Length]).ToArray());
    }

    // TightVNC Server registry şifre formatı: DES-ECB(plaintext, bit_reversed(fixed_key)).
    // Bu, TightVNC'nin (ve standart VNC'nin) registry'de şifre saklama biçimidir.
    // Çalışan normal agent (VncInstallerService) ile birebir aynı yöntem.
    // TightVNC bu değeri DES-decrypt ederek plaintext'i bulur; sonra standart VNC auth'ta
    // bit_reversed(plaintext) DES anahtarını kullanır. Backend VncAuthenticator da aynı
    // bit_reversed(plaintext) anahtarını ürettiği için challenge-response eşleşir.
    private static readonly byte[] VncFixedKey = { 0x17, 0x52, 0x6B, 0x06, 0x23, 0x4E, 0x58, 0x07 };

    private static byte ReverseBits(byte b)
    {
        byte r = 0;
        for (int i = 0; i < 8; i++) { r = (byte)((r << 1) | (b & 1)); b >>= 1; }
        return r;
    }

    private static byte[] EncryptVncPassword(string password)
    {
        var fixedKey = new byte[8];
        for (int i = 0; i < 8; i++)
            fixedKey[i] = ReverseBits(VncFixedKey[i]);

        var pwdBytes = new byte[8];
        var src = System.Text.Encoding.ASCII.GetBytes(password);
        Array.Copy(src, pwdBytes, Math.Min(src.Length, 8));

        using var des = DES.Create();
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        des.Key = fixedKey;

        var encrypted = new byte[8];
        using var encryptor = des.CreateEncryptor();
        encryptor.TransformBlock(pwdBytes, 0, 8, encrypted, 0);
        return encrypted;
    }

private static void RunProcess(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(30000);
    }
}
