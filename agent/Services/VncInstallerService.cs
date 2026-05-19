using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Orchestra.Agent.Interfaces;

namespace Orchestra.Agent.Services;

/// <summary>
/// TightVNC silent installer & password manager.
/// Installs TightVNC, generates a random password, and reports it to the backend.
/// </summary>
public sealed class VncInstallerService
{
    private readonly ILogger<VncInstallerService> _logger;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDeviceIdentityProvider _identityProvider;

    private static readonly string VncPasswordFile =
        Path.Combine(AppContext.BaseDirectory, "vnc_password.dat");

    public VncInstallerService(
        ILogger<VncInstallerService> logger,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        IDeviceIdentityProvider identityProvider)
    {
        _logger = logger;
        _config = config;
        _identityProvider = identityProvider;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Main entry point: install TightVNC if needed, configure password, report to backend.
    /// </summary>
    public async Task<(bool Success, string Output)> InstallAndConfigureAsync(CancellationToken ct = default)
    {
        var diag = new StringBuilder();
        try
        {
            _logger.LogInformation("[VNC] Starting VNC installation process...");

            // 1. Check if TightVNC is already installed
            bool alreadyInstalled = IsTightVncInstalled();
            diag.AppendLine(alreadyInstalled ? "TightVNC: zaten kurulu (repair modu)" : "TightVNC: kurulu degil (kurulum yapilacak)");

            if (!alreadyInstalled)
            {
                var msiPath = await GetTightVncMsiAsync(ct);
                if (string.IsNullOrEmpty(msiPath))
                    return (false, diag.AppendLine("HATA: TightVNC MSI bulunamadi/indirilemedi").ToString());

                var installResult = await SilentInstallAsync(msiPath, ct);
                diag.AppendLine("MSI install: " + installResult.Output);
                if (!installResult.Success) return (false, diag.ToString());
            }

            // 2. Servisi enable + auto-start moduna al
            var ensureStatus = EnsureServiceAutoStart();
            diag.AppendLine("Servis config: " + ensureStatus);

            // 3. Password yukle/uret + registry yaz + control komutlari
            var password = GetOrGeneratePassword();
            ConfigureTightVncPassword(password);
            SetPasswordViaRegistry(password);
            ApplyLoopbackFix();
            diag.AppendLine("Registry + control parametreleri yazildi (LoopbackOnly=0, RfbPort=5900, AcceptConnections=1)");

            // 4. Servisi guvenli sekilde restart et (stuck process kill fallback'li)
            var restartStatus = RestartVncServiceHard();
            diag.AppendLine("Servis restart: " + restartStatus);

            // 5. Firewall kuralini garantile
            var fwStatus = EnsureFirewallRule();
            diag.AppendLine("Firewall: " + fwStatus);

            // 6. Bind dogrulama
            var bindStatus = VerifyVncBinding();
            diag.AppendLine("Bind: " + bindStatus);
            _logger.LogInformation("[VNC] Bind status: {Status}", bindStatus);

            // 7. Backend'e raporla
            await ReportVncStatusAsync(password, ct);
            diag.AppendLine("Backend'e raporlandi");

            var ok = bindStatus.StartsWith("OK");
            diag.AppendLine(ok
                ? $"SONUC: VNC hazir. Port 5900 dinleniyor. VNC_PWD={password}"
                : $"SONUC UYARI: bind durumu '{bindStatus}'. VNC_PWD={password}");
            return (ok, diag.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VNC] VNC installation failed");
            diag.AppendLine("EXCEPTION: " + ex.Message);
            return (false, diag.ToString());
        }
    }

    private bool IsTightVncInstalled()
    {
        // Check registry for TightVNC
        var paths = new[]
        {
            @"SOFTWARE\TightVNC",
            @"SOFTWARE\WOW6432Node\TightVNC"
        };

        foreach (var path in paths)
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key != null) return true;
        }

        // Check if tvnserver.exe exists in common locations
        var exePaths = new[]
        {
            @"C:\Program Files\TightVNC\tvnserver.exe",
            @"C:\Program Files (x86)\TightVNC\tvnserver.exe"
        };

        return exePaths.Any(File.Exists);
    }

    private async Task<string?> GetTightVncMsiAsync(CancellationToken ct)
    {
        // First check if MSI is bundled with the agent
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "tools", "tightvnc.msi");
        if (File.Exists(bundledPath))
        {
            _logger.LogInformation("[VNC] Using bundled TightVNC MSI: {Path}", bundledPath);
            return bundledPath;
        }

        // Try downloading from backend
        var backendUrl = _config.GetValue<string>("Agent:BackendUrl")?.TrimEnd('/');
        if (string.IsNullOrEmpty(backendUrl))
        {
            _logger.LogError("[VNC] BackendUrl not configured");
            return null;
        }

        try
        {
            var downloadUrl = $"{backendUrl}/api/updates/vnc-installer";
            _logger.LogInformation("[VNC] Downloading TightVNC from {Url}", downloadUrl);

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            var response = await client.GetAsync(downloadUrl, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[VNC] Download failed: {Status}. TightVNC MSI must be provided in agent/tools/ directory", response.StatusCode);
                return null;
            }

            var tempMsi = Path.Combine(Path.GetTempPath(), "tightvnc_mudosoft.msi");
            await using var fs = new FileStream(tempMsi, FileMode.Create);
            await response.Content.CopyToAsync(fs, ct);

            _logger.LogInformation("[VNC] Downloaded TightVNC MSI to {Path}", tempMsi);
            return tempMsi;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VNC] Failed to download TightVNC MSI");
            return null;
        }
    }

    private async Task<(bool Success, string Output)> SilentInstallAsync(string msiPath, CancellationToken ct)
    {
        _logger.LogInformation("[VNC] Installing TightVNC silently from {Path}", msiPath);

        // Silent install with server only (no viewer), accept EULA
        // SET_USEFIREWALL=1 to add firewall exception
        // SET_ACCEPTHTTPCONNECTIONS=0 to disable Java viewer
        var args = $"/i \"{msiPath}\" /quiet /norestart " +
                   "ADDLOCAL=Server " +
                   "SET_USEFIREWALL=1 " +
                   "SET_ACCEPTHTTPCONNECTIONS=0 " +
                   "SET_CONNECTPRIORITY=0";

        var psi = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            var process = Process.Start(psi)!;

            // Wait up to 3 minutes for install
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));

            await process.WaitForExitAsync(timeoutCts.Token);

            if (process.ExitCode == 0 || process.ExitCode == 3010) // 3010 = reboot required but OK
            {
                _logger.LogInformation("[VNC] TightVNC installed successfully (exit code: {Code})", process.ExitCode);
                return (true, "TightVNC kuruldu");
            }

            var error = await process.StandardError.ReadToEndAsync(ct);
            _logger.LogError("[VNC] Install failed with exit code {Code}: {Error}", process.ExitCode, error);
            return (false, $"Kurulum hatası (kod: {process.ExitCode}): {error}");
        }
        catch (OperationCanceledException)
        {
            return (false, "Kurulum zaman aşımına uğradı");
        }
        catch (Exception ex)
        {
            return (false, $"Kurulum başlatılamadı: {ex.Message}");
        }
    }

    /// <summary>
    /// Generate a random 8-character alphanumeric password or load existing one.
    /// Password is protected with DPAPI (machine-scope encryption).
    /// </summary>
    private string GetOrGeneratePassword()
    {
        // Check if we already have a saved password (DPAPI encrypted)
        if (File.Exists(VncPasswordFile))
        {
            try
            {
                var encryptedBytes = File.ReadAllBytes(VncPasswordFile);
                var decryptedBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                    encryptedBytes, null, System.Security.Cryptography.DataProtectionScope.LocalMachine);
                var saved = Encoding.UTF8.GetString(decryptedBytes).Trim();
                if (!string.IsNullOrEmpty(saved))
                {
                    _logger.LogInformation("[VNC] Using existing VNC password (DPAPI protected)");
                    return saved;
                }
            }
            catch
            {
                // Eski plaintext dosya olabilir — okumayı dene, sonra DPAPI ile yeniden kaydet
                try
                {
                    var plaintext = File.ReadAllText(VncPasswordFile).Trim();
                    if (!string.IsNullOrEmpty(plaintext) && plaintext.Length <= 20)
                    {
                        _logger.LogInformation("[VNC] Migrating plaintext password to DPAPI");
                        SavePasswordWithDpapi(plaintext);
                        return plaintext;
                    }
                }
                catch { }
            }
        }

        // Generate new random password
        const string chars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var password = new string(RandomNumberGenerator.GetBytes(8)
            .Select(b => chars[b % chars.Length])
            .ToArray());

        // Save with DPAPI
        SavePasswordWithDpapi(password);
        return password;
    }

    private void SavePasswordWithDpapi(string password)
    {
        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(password);
            var encryptedBytes = System.Security.Cryptography.ProtectedData.Protect(
                plainBytes, null, System.Security.Cryptography.DataProtectionScope.LocalMachine);
            File.WriteAllBytes(VncPasswordFile, encryptedBytes);
            _logger.LogInformation("[VNC] VNC password saved with DPAPI protection");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VNC] Could not save VNC password file with DPAPI");
        }
    }

    /// <summary>
    /// Configure TightVNC password via registry.
    /// TightVNC stores passwords as DES-encrypted values in the registry.
    /// We use the tvnserver -controlservice -setparam approach for simplicity.
    /// </summary>
    private void ConfigureTightVncPassword(string password)
    {
        _logger.LogInformation("[VNC] Configuring TightVNC password...");

        var tvnPath = FindTvnServerPath();
        if (tvnPath == null)
        {
            _logger.LogWarning("[VNC] tvnserver.exe not found, trying registry approach");
            SetPasswordViaRegistry(password);
            return;
        }

        // Use tvnserver to set password
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = tvnPath,
                Arguments = $"-controlservice -setparam Password={password}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            process?.WaitForExit(10000);

            // Also set to accept connections
            psi.Arguments = "-controlservice -setparam AcceptConnections=1";
            process = Process.Start(psi);
            process?.WaitForExit(10000);

            // Disable HTTP connections (Java viewer)
            psi.Arguments = "-controlservice -setparam AcceptHttpConnections=0";
            process = Process.Start(psi);
            process?.WaitForExit(10000);

            // Set to use port 5900
            psi.Arguments = "-controlservice -setparam RfbPort=5900";
            process = Process.Start(psi);
            process?.WaitForExit(10000);

            _logger.LogInformation("[VNC] TightVNC configured via tvnserver control");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VNC] Failed to configure TightVNC via control service");
            SetPasswordViaRegistry(password);
        }
    }

    private void SetPasswordViaRegistry(string password)
    {
        try
        {
            // TightVNC stores password as DES-encrypted 8 bytes in registry
            var encryptedPassword = EncryptVncPassword(password);

            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\TightVNC\Server");
            if (key != null)
            {
                key.SetValue("Password", encryptedPassword, RegistryValueKind.Binary);
                key.SetValue("AcceptConnections", 1, RegistryValueKind.DWord);
                key.SetValue("AcceptHttpConnections", 0, RegistryValueKind.DWord);
                key.SetValue("RfbPort", 5900, RegistryValueKind.DWord);
                // 5900'in 0.0.0.0'a bind etmesini garantile — varsayilan bazi kurulumlarda 127.0.0.1 only oluyor
                key.SetValue("LoopbackOnly", 0, RegistryValueKind.DWord);
                key.SetValue("AllowLoopback", 1, RegistryValueKind.DWord);
                _logger.LogInformation("[VNC] TightVNC configured via registry (LoopbackOnly=0 forced)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VNC] Failed to configure TightVNC via registry");
        }
    }

    /// <summary>
    /// tvnserver hâlâ ayakta ise -controlservice ile parametreleri set et.
    /// SetPasswordViaRegistry zaten registry'yi yazıyor, ama tvnserver service runtime cache'i
    /// nedeniyle bazen registry'yi okumuyor — bu sayede çift kanal.
    /// </summary>
    private void ApplyLoopbackFix()
    {
        var tvnPath = FindTvnServerPath();
        if (tvnPath == null) return;
        try
        {
            foreach (var param in new[] { "LoopbackOnly=0", "AllowLoopback=1", "RfbPort=5900", "AcceptConnections=1" })
            {
                var psi = new ProcessStartInfo
                {
                    FileName = tvnPath,
                    Arguments = $"-controlservice -setparam {param}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                p?.WaitForExit(8000);
            }
            _logger.LogInformation("[VNC] LoopbackOnly fix applied via tvnserver control");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VNC] tvnserver control LoopbackOnly fix failed");
        }
    }

    /// <summary>
    /// tvnserver servisinin StartType=auto olmasini ve mevcut oldugunu garantile.
    /// </summary>
    private string EnsureServiceAutoStart()
    {
        try
        {
            var query = RunProcess("sc.exe", "query tvnserver", 5000);
            if (query.exitCode != 0)
                return $"servis bulunamadi (sc query rc={query.exitCode}, out={Truncate(query.stdout, 120)})";

            var cfg = RunProcess("sc.exe", "config tvnserver start= auto", 5000);
            return cfg.exitCode == 0
                ? "start=auto olarak ayarlandi"
                : $"sc config rc={cfg.exitCode}: {Truncate(cfg.stderr, 120)}";
        }
        catch (Exception ex)
        {
            return "exception: " + ex.Message;
        }
    }

    /// <summary>
    /// tvnserver'i guvenli sekilde restart eder. net stop fail olursa process kill fallback.
    /// </summary>
    private string RestartVncServiceHard()
    {
        var report = new StringBuilder();
        try
        {
            // 1) Once nazikce durdur
            var stop = RunProcess("sc.exe", "stop tvnserver", 15000);
            report.Append("stop rc=").Append(stop.exitCode);
            if (stop.exitCode != 0)
            {
                // Servis zaten durmus olabilir (1062 = service not started) — bunu hata sayma
                if (!stop.stdout.Contains("1062") && !stop.stderr.Contains("1062"))
                {
                    report.Append(" (stuck olabilir, process kill)");
                    KillTvnProcesses();
                }
            }

            // Servisin gercekten durdugunu dogrula (max 8sn)
            for (int i = 0; i < 8; i++)
            {
                var q = RunProcess("sc.exe", "query tvnserver", 3000);
                if (q.stdout.Contains("STOPPED")) break;
                Thread.Sleep(1000);
            }

            // Eger hala calisiyorsa kill
            if (IsTvnRunning())
            {
                report.Append(" | hala calisiyor → process kill");
                KillTvnProcesses();
                Thread.Sleep(1500);
            }

            // 2) Baslat
            var start = RunProcess("sc.exe", "start tvnserver", 15000);
            report.Append(" | start rc=").Append(start.exitCode);
            if (start.exitCode != 0)
                report.Append(" stderr=").Append(Truncate(start.stderr, 100));

            // 3) RUNNING durumunu bekle
            for (int i = 0; i < 10; i++)
            {
                var q = RunProcess("sc.exe", "query tvnserver", 3000);
                if (q.stdout.Contains("RUNNING")) { report.Append(" | RUNNING"); break; }
                Thread.Sleep(1000);
            }

            return report.ToString();
        }
        catch (Exception ex)
        {
            return report.Append(" | exception ").Append(ex.Message).ToString();
        }
    }

    private void KillTvnProcesses()
    {
        foreach (var name in new[] { "tvnserver", "tvnserver_64" })
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(true); p.WaitForExit(5000); }
                    catch { /* yetki yok ya da zaten oldu */ }
                }
            }
            catch { /* ignore */ }
        }
    }

    private bool IsTvnRunning()
    {
        try { return Process.GetProcessesByName("tvnserver").Length > 0
                  || Process.GetProcessesByName("tvnserver_64").Length > 0; }
        catch { return false; }
    }

    /// <summary>
    /// Windows Firewall'da 5900/TCP icin gelen baglanti kuralini garantile.
    /// Mevcut kurali silip yeniden olusturur (idempotent).
    /// </summary>
    private string EnsureFirewallRule()
    {
        const string ruleName = "Orchestra TightVNC 5900";
        try
        {
            // Once varsa sil (sessizce gec)
            RunProcess("netsh.exe", $"advfirewall firewall delete rule name=\"{ruleName}\"", 5000);

            var add = RunProcess("netsh.exe",
                $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport=5900 profile=any",
                5000);
            return add.exitCode == 0 ? "kural eklendi" : $"netsh rc={add.exitCode}: {Truncate(add.stdout + add.stderr, 100)}";
        }
        catch (Exception ex)
        {
            return "exception: " + ex.Message;
        }
    }

    private static (int exitCode, string stdout, string stderr) RunProcess(string file, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return (-1, "", "Process.Start returned null");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return (-2, stdout, stderr + " | TIMEOUT");
            }
            return (p.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-3, "", ex.Message);
        }
    }

    private static string Truncate(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= n ? s : s.Substring(0, n) + "…";
    }

    /// <summary>
    /// 5900 portu 0.0.0.0'a bind olmus mu doğrula.
    /// </summary>
    private string VerifyVncBinding()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-an",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            if (p == null) return "netstat çalıştırılamadı";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);

            var lines = output.Split('\n')
                .Where(l => l.Contains(":5900") && l.Contains("LISTENING"))
                .Select(l => l.Trim())
                .ToList();
            if (lines.Count == 0) return "5900 dinleyici bulunamadı";
            if (lines.Any(l => l.StartsWith("TCP    0.0.0.0:5900"))) return "OK 0.0.0.0:5900 LISTENING";
            return "UYARI " + string.Join(" | ", lines);
        }
        catch (Exception ex) { return "verify failed: " + ex.Message; }
    }

    /// <summary>
    /// VNC DES password encryption (standard VNC password encoding).
    /// VNC uses a fixed DES key derived from the password itself with bit-reversal.
    /// </summary>
    private static byte[] EncryptVncPassword(string password)
    {
        // TightVNC password storage: DES_ECB(key=fixedKey, plaintext=passwordBytes)
        // The fixed key from TightVNC source (CryptoUtils.cpp): {23,82,107,6,35,78,88,7}
        // TightVNC's DesCipher bit-reverses each byte of the key before DES
        var fixedKey = new byte[8] { 0x17, 0x52, 0x6B, 0x06, 0x23, 0x4E, 0x58, 0x07 };
        for (int i = 0; i < 8; i++)
            fixedKey[i] = ReverseBits(fixedKey[i]);

        // Password padded with nulls to 8 bytes (plaintext for DES)
        var pwdBytes = new byte[8];
        var passBytes = Encoding.ASCII.GetBytes(password);
        Array.Copy(passBytes, pwdBytes, Math.Min(passBytes.Length, 8));

        using var des = System.Security.Cryptography.DES.Create();
        des.Mode = System.Security.Cryptography.CipherMode.ECB;
        des.Padding = System.Security.Cryptography.PaddingMode.None;
        des.Key = fixedKey;

        using var encryptor = des.CreateEncryptor();
        var encrypted = new byte[8];
        encryptor.TransformBlock(pwdBytes, 0, 8, encrypted, 0);

        return encrypted;
    }

    private static byte ReverseBits(byte b)
    {
        byte reversed = 0;
        for (int j = 0; j < 8; j++)
        {
            if ((b & (1 << j)) != 0)
                reversed |= (byte)(1 << (7 - j));
        }
        return reversed;
    }

    private string? FindTvnServerPath()
    {
        var paths = new[]
        {
            @"C:\Program Files\TightVNC\tvnserver.exe",
            @"C:\Program Files (x86)\TightVNC\tvnserver.exe"
        };

        return paths.FirstOrDefault(File.Exists);
    }

    private void EnsureServiceRunning()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "net",
                Arguments = "start tvnserver",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            process?.WaitForExit(15000);

            _logger.LogInformation("[VNC] TightVNC service start requested (may already be running)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VNC] Could not start TightVNC service");
        }
    }

    private async Task ReportVncStatusAsync(string password, CancellationToken ct)
    {
        var backendUrl = _config.GetValue<string>("Agent:BackendUrl")?.TrimEnd('/');
        var deviceId = _identityProvider.GetDeviceId();

        if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(deviceId))
        {
            _logger.LogWarning("[VNC] Cannot report VNC status: BackendUrl={Url} DeviceId={Id}", backendUrl, deviceId);
            return;
        }

        var payload = new
        {
            deviceId,
            installed = true,
            password,
            port = 5900
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync($"{backendUrl}/api/agent/vnc-status", content, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[VNC] VNC status reported to backend successfully");
            }
            else
            {
                _logger.LogWarning("[VNC] Backend returned {Status} for VNC status report", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VNC] Failed to report VNC status to backend");
        }
    }
}
