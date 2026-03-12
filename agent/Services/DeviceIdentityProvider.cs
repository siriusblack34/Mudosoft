using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Management;
using Microsoft.Extensions.Logging;
using Mudosoft.Agent.Interfaces;

namespace Mudosoft.Agent.Services
{
    public sealed class DeviceIdentityProvider : IDeviceIdentityProvider
    {
        private static readonly string DeviceIdFilePath = Path.Combine(AppContext.BaseDirectory, "device_id.txt");
        private readonly ILogger<DeviceIdentityProvider> _logger;
        private readonly string _deviceId;

        public DeviceIdentityProvider(ILogger<DeviceIdentityProvider> logger)
        {
            _logger = logger;
            _logger.LogInformation("Device ID file path: {Path}", DeviceIdFilePath);
            _deviceId = GetOrCreateDeviceId();
        }

        public string GetDeviceId()
        {
            return _deviceId;
        }

        private string GetOrCreateDeviceId()
        {
            try
            {
                string hardwareId = ComputeHardwareId();
                _logger.LogInformation("Hesaplanan Donanim ID: {HardwareId}", hardwareId);

                if (File.Exists(DeviceIdFilePath))
                {
                    string existingId = File.ReadAllText(DeviceIdFilePath).Trim();
                    if (existingId == hardwareId)
                    {
                        _logger.LogInformation("Kalici cihaz ID dogrulandi: {DeviceId}", existingId);
                        return existingId;
                    }
                    else
                    {
                        _logger.LogWarning("Mevcut ID ({Existing}) donanim ID ({HardwareId}) ile uyusmuyor.", existingId, hardwareId);
                    }
                }

                File.WriteAllText(DeviceIdFilePath, hardwareId);
                _logger.LogWarning("Donanim tabanli yeni cihaz ID kaydedildi: {DeviceId}", hardwareId);
                return hardwareId;
            }
            catch (Exception ex)
            {
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
