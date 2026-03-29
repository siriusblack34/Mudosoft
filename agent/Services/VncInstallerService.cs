using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Mudosoft.Agent.Interfaces;

namespace Mudosoft.Agent.Services;

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
        try
        {
            _logger.LogInformation("[VNC] Starting VNC installation process...");

            // 1. Check if TightVNC is already installed
            bool alreadyInstalled = IsTightVncInstalled();

            if (!alreadyInstalled)
            {
                // 2. Download TightVNC MSI from backend or use bundled one
                var msiPath = await GetTightVncMsiAsync(ct);
                if (string.IsNullOrEmpty(msiPath))
                {
                    return (false, "TightVNC MSI bulunamadı veya indirilemedi");
                }

                // 3. Silent install TightVNC (server only, no viewer)
                var installResult = await SilentInstallAsync(msiPath, ct);
                if (!installResult.Success)
                {
                    return installResult;
                }
            }
            else
            {
                _logger.LogInformation("[VNC] TightVNC is already installed");
            }

            // 4. Generate or load existing password
            var password = GetOrGeneratePassword();

            // 5. Configure TightVNC password via registry
            ConfigureTightVncPassword(password);

            // 6. Ensure TightVNC service is running
            EnsureServiceRunning();

            // 7. Report to backend
            await ReportVncStatusAsync(password, ct);

            _logger.LogInformation("[VNC] VNC installation and configuration complete");
            return (true, $"VNC kuruldu ve yapılandırıldı. Port: 5900 | VNC_PWD={password}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VNC] VNC installation failed");
            return (false, $"VNC kurulum hatası: {ex.Message}");
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
    /// </summary>
    private string GetOrGeneratePassword()
    {
        // Check if we already have a saved password
        if (File.Exists(VncPasswordFile))
        {
            try
            {
                var saved = File.ReadAllText(VncPasswordFile).Trim();
                if (!string.IsNullOrEmpty(saved))
                {
                    _logger.LogInformation("[VNC] Using existing VNC password");
                    return saved;
                }
            }
            catch { }
        }

        // Generate new random password
        const string chars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var password = new string(RandomNumberGenerator.GetBytes(8)
            .Select(b => chars[b % chars.Length])
            .ToArray());

        // Save it
        try
        {
            File.WriteAllText(VncPasswordFile, password);
            _logger.LogInformation("[VNC] Generated and saved new VNC password");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VNC] Could not save VNC password file");
        }

        return password;
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
                _logger.LogInformation("[VNC] TightVNC configured via registry");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VNC] Failed to configure TightVNC via registry");
        }
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
