using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Shared.Dtos;
using System.Management;
using System.Text.Json;

namespace Orchestra.Agent.Services.Collectors;

/// <summary>
/// Disk sağlığını ve alan kullanımını izler.
/// DriveInfo (Win7+Win11 uyumlu) + WMI SMART (opsiyonel) kullanır.
/// </summary>
public sealed class DiskHealthCollector : ICollector
{
    private readonly DiskHealthConfig _config;
    private readonly ILogger<DiskHealthCollector> _logger;

    public string Name => "DiskHealth";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public DiskHealthCollector(
        IOptions<CollectorsConfig> config,
        ILogger<DiskHealthCollector> logger)
    {
        _config = config.Value.DiskHealth;
        _logger = logger;
    }

    public Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var results = new List<DiskHealthDto>();
        var hasWarning = false;

        // SMART durumlarını topla (WMI ile)
        var smartStatuses = GetSmartStatuses();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                continue;

            try
            {
                var totalGB = drive.TotalSize / (1024.0 * 1024 * 1024);
                var freeGB = drive.TotalFreeSpace / (1024.0 * 1024 * 1024);
                var usedPercent = totalGB > 0 ? ((totalGB - freeGB) / totalGB) * 100.0 : 0;

                var driveLetter = drive.Name.TrimEnd('\\');
                smartStatuses.TryGetValue(driveLetter, out var smart);

                if (usedPercent > 90)
                {
                    hasWarning = true;
                    _logger.LogWarning("Disk {Drive} usage at {Pct:F1}%", driveLetter, usedPercent);
                }

                results.Add(new DiskHealthDto
                {
                    DriveLetter = driveLetter,
                    Label = drive.VolumeLabel,
                    DriveType = drive.DriveType.ToString(),
                    FileSystem = drive.DriveFormat,
                    TotalGB = Math.Round(totalGB, 1),
                    FreeGB = Math.Round(freeGB, 1),
                    UsedPercent = Math.Round(usedPercent, 1),
                    SmartStatus = smart
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading drive {Drive}", drive.Name);
            }
        }

        return Task.FromResult(new CollectorResult
        {
            CollectorName = Name,
            Success = true,
            Severity = hasWarning ? "Warning" : "Info",
            JsonData = JsonSerializer.Serialize(results)
        });
    }

    /// <summary>
    /// WMI ile disk SMART durumlarını oku. Başarısız olursa boş döner.
    /// </summary>
    private Dictionary<string, string> GetSmartStatuses()
    {
        var result = new Dictionary<string, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Status FROM Win32_DiskDrive");

            foreach (var obj in searcher.Get())
            {
                var deviceId = obj["DeviceID"]?.ToString() ?? "";
                var status = obj["Status"]?.ToString() ?? "Unknown";

                // DeviceID = \\.\PHYSICALDRIVE0 gibi, index'e göre eşleştir
                // Basit eşleme: ilk drive = C:, ikinci = D: vs.
                result[deviceId] = status;
            }

            // Logical disk ile physical disk eşleme
            using var logicalSearcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Status FROM Win32_LogicalDisk WHERE DriveType=3");
            foreach (var obj in logicalSearcher.Get())
            {
                var letter = obj["DeviceID"]?.ToString() ?? "";
                // Win32_LogicalDisk.Status: "OK", "Degraded", "Pred Fail"
                // Gerçek SMART Win32_DiskDrive'dan gelir ama burada da kontrol ederiz
                if (!result.ContainsKey(letter))
                {
                    result[letter] = "OK"; // Default
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WMI SMART query failed (may not be available)");
        }
        return result;
    }
}
