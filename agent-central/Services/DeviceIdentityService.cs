using Microsoft.Extensions.Options;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace Orchestra.CentralAgent.Services;

/// <summary>
/// Cihaza özgü kalıcı DeviceId üretir/yükler. MAC adresi + hostname hash'i kullanır.
/// </summary>
public class DeviceIdentityService
{
    private readonly CentralAgentConfig _config;
    private string? _cachedId;

    public DeviceIdentityService(IOptions<CentralAgentConfig> config)
    {
        _config = config.Value;
    }

    public string GetDeviceId()
    {
        if (_cachedId != null) return _cachedId;

        var idFile = _config.DeviceIdFile;
        if (File.Exists(idFile))
        {
            _cachedId = File.ReadAllText(idFile).Trim();
            if (!string.IsNullOrEmpty(_cachedId)) return _cachedId;
        }

        // Yeni ID oluştur: MAC + hostname hash
        var mac      = GetPrimaryMac();
        var hostname = Environment.MachineName;
        var raw      = $"central:{mac}:{hostname}";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        _cachedId = BitConverter.ToString(hash).Replace("-", "").ToLower()[..32];

        var dir = Path.GetDirectoryName(idFile)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(idFile, _cachedId);

        return _cachedId;
    }

    private static string GetPrimaryMac()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderBy(n => n.NetworkInterfaceType)
            .Select(n => n.GetPhysicalAddress().ToString())
            .FirstOrDefault() ?? "000000000000";
    }
}
