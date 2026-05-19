using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Management;
using Microsoft.Extensions.Logging;
using Orchestra.Agent.Interfaces;

namespace Orchestra.Agent.Services
{
    public sealed class DeviceIdentityProvider : IDeviceIdentityProvider
    {
        private static readonly string DeviceIdFilePath = Path.Combine(AppContext.BaseDirectory, "device_id.txt");
        private readonly ILogger<DeviceIdentityProvider> _logger;
        private readonly string _deviceId;

        // Diagnostic boot trace — agent service start'inda hangi adimda takildigimizi gormek icin
        private static readonly string DiagLogPath = Path.Combine(AppContext.BaseDirectory, "mudosoft_helper.log");
        private static void DiagLog(string msg)
        {
            try { File.AppendAllText(DiagLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: [DeviceIdentity] {msg}{Environment.NewLine}"); } catch { }
        }

        public DeviceIdentityProvider(ILogger<DeviceIdentityProvider> logger)
        {
            DiagLog("ctor begin");
            _logger = logger;
            _logger.LogInformation("Device ID file path: {Path}", DeviceIdFilePath);
            _deviceId = GetOrCreateDeviceId();
            DiagLog($"ctor end, id={_deviceId}");
        }

        public string GetDeviceId()
        {
            return _deviceId;
        }

        private string GetOrCreateDeviceId()
        {
            // Cache-first: eger device_id.txt varsa WMI'ye hic gitme.
            // Eski kod her boot'ta WMI calistiriyordu; mağaza POS'larında WMI yavas/hung olabiliyor
            // ve bu boot'u 120sn+ blocklayip SCM kill'ine sebep oluyordu.
            try
            {
                if (File.Exists(DeviceIdFilePath))
                {
                    var cachedId = File.ReadAllText(DeviceIdFilePath).Trim();
                    if (!string.IsNullOrWhiteSpace(cachedId) && cachedId.Length >= 16)
                    {
                        DiagLog($"cached id loaded: {cachedId}");
                        _logger.LogInformation("Kalici cihaz ID cache'den okundu: {DeviceId}", cachedId);
                        return cachedId;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagLog($"cache read failed: {ex.Message}");
                _logger.LogWarning(ex, "Cache okuma basarisiz, WMI'ye dusulecek.");
            }

            // Cache yok ya da bozuk — WMI ile hesapla (yalnizca ilk kurulumda)
            DiagLog("cache miss, computing hardware id via WMI");
            try
            {
                string hardwareId = ComputeHardwareId();
                DiagLog($"WMI hardware id: {hardwareId}");
                _logger.LogInformation("Hesaplanan Donanim ID: {HardwareId}", hardwareId);

                File.WriteAllText(DeviceIdFilePath, hardwareId);
                _logger.LogWarning("Donanim tabanli yeni cihaz ID kaydedildi: {DeviceId}", hardwareId);
                DiagLog("hardware id persisted to cache");
                return hardwareId;
            }
            catch (Exception ex)
            {
                DiagLog($"WMI failed: {ex.Message}");
                _logger.LogError(ex, "Cihaz ID hesabi basarisiz. Gecici ID kullaniliyor.");
                return Guid.NewGuid().ToString("N");
            }
        }

        private string ComputeHardwareId()
        {
            try
            {
                if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    return ComputeFallbackId();

                string uuid = GetWmiValue("Win32_ComputerSystemProduct", "UUID");
                string biosSerial = GetWmiValue("Win32_BIOS", "SerialNumber");

                if (string.IsNullOrWhiteSpace(uuid) && string.IsNullOrWhiteSpace(biosSerial))
                    return ComputeFallbackId();

                string rawData = $"{uuid}-{biosSerial}-MudosoftAgent";

                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                    StringBuilder builder = new StringBuilder();
                    for (int i = 0; i < bytes.Length; i++)
                        builder.Append(bytes[i].ToString("x2"));
                    return builder.ToString().Substring(0, 32);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Donanim ID uretilemedi. Fallback'e geciliyor.");
                return ComputeFallbackId();
            }
        }

        private string ComputeFallbackId()
        {
            try 
            {
                string rawData = $"{Environment.MachineName}-{Environment.OSVersion.VersionString}-MudosoftFallback";
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                    StringBuilder builder = new StringBuilder();
                    for (int i = 0; i < bytes.Length; i++)
                        builder.Append(bytes[i].ToString("x2"));
                    return builder.ToString().Substring(0, 32);
                }
            }
            catch 
            {
                return Guid.NewGuid().ToString("N");
            }
        }

        private string GetWmiValue(string wmiClass, string wmiProperty)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher($"SELECT {wmiProperty} FROM {wmiClass}"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj[wmiProperty]?.ToString()?.Trim() ?? "";
                    }
                }
            }
            catch { }
            return "";
        }
    }
}
