using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly IDeviceRepository _deviceRepository;

    public ReportsController(OrchestraDbContext db, IDeviceRepository deviceRepository)
    {
        _db = db;
        _deviceRepository = deviceRepository;
    }

    [HttpGet("store-outages")]
    public async Task<IActionResult> GetStoreOutages([FromQuery] int days = 30)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Max(1, days));

        var incidents = await _db.StoreOfflineLogs
            .AsNoTracking()
            .Where(x => x.OfflineAt >= since)
            .OrderByDescending(x => x.OfflineAt)
            .Select(x => new StoreOutageIncidentDto
            {
                Id = x.Id,
                StoreCode = x.StoreCode,
                StoreName = x.StoreName,
                OfflineKasaCount = x.OfflineKasaCount,
                OfflineAt = x.OfflineAt,
                OnlineAt = x.OnlineAt,
                DurationMinutes = x.DurationMinutes,
                IsStillOffline = x.OnlineAt == null
            })
            .ToListAsync();

        var summary = incidents
            .GroupBy(x => new { x.StoreCode, x.StoreName })
            .Select(g => new StoreOutageSummaryDto
            {
                StoreCode = g.Key.StoreCode,
                StoreName = g.Key.StoreName,
                IncidentCount = g.Count(),
                TotalOfflineMinutes = g.Sum(x => x.DurationMinutes ?? 0),
                AverageOfflineMinutes = g.Where(x => x.DurationMinutes.HasValue).Any()
                    ? Math.Round(g.Where(x => x.DurationMinutes.HasValue).Average(x => x.DurationMinutes ?? 0), 1)
                    : 0,
                LastOfflineAt = g.Max(x => x.OfflineAt),
                LastOnlineAt = g.Max(x => x.OnlineAt),
                IsCurrentlyOffline = g.Any(x => x.IsStillOffline),
                MaxOfflineKasaCount = g.Max(x => x.OfflineKasaCount)
            })
            .OrderByDescending(x => x.IncidentCount)
            .ThenByDescending(x => x.TotalOfflineMinutes)
            .ToList();

        return Ok(new StoreOutageReportDto
        {
            PeriodDays = days,
            GeneratedAtUtc = DateTime.UtcNow,
            TotalIncidents = incidents.Count,
            CurrentlyOfflineStoreCount = summary.Count(x => x.IsCurrentlyOffline),
            Summary = summary,
            Incidents = incidents
        });
    }

    [HttpGet("hardware-inventory")]
    public async Task<IActionResult> GetHardwareInventory()
    {
        var allDevices = _deviceRepository.GetAll()
            .Where(x => !string.IsNullOrWhiteSpace(x.AgentVersion))
            .ToList();

        var storeNames = await _db.StoreDevices
            .AsNoTracking()
            .Where(x => x.StoreCode > 0 && !string.IsNullOrWhiteSpace(x.StoreName))
            .GroupBy(x => x.StoreCode)
            .Select(g => new { StoreCode = g.Key, StoreName = g.Select(x => x.StoreName).FirstOrDefault() })
            .ToListAsync();

        var storeNameMap = storeNames
            .Where(x => !string.IsNullOrWhiteSpace(x.StoreName))
            .ToDictionary(x => x.StoreCode, x => x.StoreName!);

        var devices = allDevices
            .Where(x => !string.IsNullOrWhiteSpace(x.AgentVersion))
            .Select(x => new HardwareInventoryRowDto
            {
                DeviceId = x.Id,
                Hostname = x.Hostname,
                StoreCode = x.StoreCode,
                StoreName = ResolveStoreName(x.StoreCode, x.StoreName, storeNameMap),
                Type = x.Type.ToString(),
                IpAddress = x.IpAddress,
                Os = x.Os,
                AgentVersion = x.AgentVersion,
                CpuModel = x.CpuModel,
                GpuModel = x.GpuModel,
                TotalRamMB = x.TotalRamMB,
                TotalDiskGB = x.TotalDiskGB,
                TotalDiskDGB = x.TotalDiskDGB,
                CpuUsagePercent = SafePercent(x.CurrentCpuUsagePercent),
                RamUsagePercent = SafePercent(x.CurrentRamUsagePercent),
                DiskUsagePercent = SafePercent(x.CurrentDiskUsagePercent),
                DiskDUsagePercent = SafeNullablePercent(x.CurrentDiskDUsagePercent),
                HealthStatus = x.HealthStatus,
                HealthScore = x.HealthScore,
                Online = x.Online,
                LastSeen = x.LastSeen,
                LastLoggedInUser = x.LastLoggedInUser,
                SystemBootTime = x.SystemBootTime,
                VncInstalled = x.VncInstalled
            })
            .OrderBy(x => x.StoreCode)
            .ThenBy(x => x.Type)
            .ThenBy(x => x.Hostname)
            .ToList();

        return Ok(new HardwareInventoryReportDto
        {
            GeneratedAtUtc = DateTime.UtcNow,
            TotalDevices = devices.Count,
            OnlineDevices = devices.Count(x => x.Online),
            CriticalDevices = devices.Count(x => string.Equals(x.HealthStatus, "Critical", StringComparison.OrdinalIgnoreCase)),
            Rows = devices
        });
    }

    [HttpGet("fault-density")]
    public async Task<IActionResult> GetFaultDensity([FromQuery] int days = 30)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Max(1, days));
        var devices = _deviceRepository.GetAll()
            .Where(x => !string.IsNullOrWhiteSpace(x.AgentVersion))
            .ToList();

        var outageStats = await _db.StoreOfflineLogs
            .AsNoTracking()
            .Where(x => x.OfflineAt >= since)
            .GroupBy(x => new { x.StoreCode, x.StoreName })
            .Select(g => new
            {
                g.Key.StoreCode,
                g.Key.StoreName,
                IncidentCount = g.Count(),
                TotalOfflineMinutes = g.Sum(x => x.DurationMinutes ?? 0),
                LastOfflineAt = g.Max(x => x.OfflineAt)
            })
            .ToListAsync();

        var storeNameMap = devices
            .Where(x => x.StoreCode > 0 && !string.IsNullOrWhiteSpace(x.StoreName))
            .GroupBy(x => x.StoreCode)
            .ToDictionary(g => g.Key, g => g.First().StoreName ?? $"Magaza {g.Key}");

        var deviceRows = devices
            .Select(device =>
            {
                var reasons = BuildDeviceIssueReasons(device);
                return new FaultDensityDeviceDto
                {
                    DeviceId = device.Id,
                    Hostname = device.Hostname,
                    StoreCode = device.StoreCode,
                    StoreName = ResolveStoreName(device.StoreCode, device.StoreName, storeNameMap),
                    Type = device.Type.ToString(),
                    Online = device.Online,
                    IsTemporarilyClosed = device.IsTemporarilyClosed,
                    HealthStatus = device.HealthStatus,
                    HealthScore = device.HealthScore,
                    CpuUsagePercent = SafePercent(device.CurrentCpuUsagePercent),
                    RamUsagePercent = SafePercent(device.CurrentRamUsagePercent),
                    DiskUsagePercent = SafePercent(device.CurrentDiskUsagePercent),
                    LastSeen = device.LastSeen,
                    IssueScore = reasons.Sum(x => x.Score),
                    IssueReasons = reasons.Select(x => x.Label).ToList()
                };
            })
            .OrderByDescending(x => x.IssueScore)
            .ThenByDescending(x => x.LastSeen)
            .ToList();

        var storeRows = devices
            .GroupBy(x => x.StoreCode)
            .Select(group =>
            {
                var storeCode = group.Key;
                var deviceIssues = deviceRows.Where(x => x.StoreCode == storeCode).ToList();
                var outage = outageStats.FirstOrDefault(x => x.StoreCode == storeCode);
                var currentOfflineDevices = group.Count(x => !x.Online && !x.IsTemporarilyClosed);
                var criticalDevices = group.Count(x => string.Equals(x.HealthStatus, "Critical", StringComparison.OrdinalIgnoreCase));
                var warningDevices = group.Count(x => string.Equals(x.HealthStatus, "Warning", StringComparison.OrdinalIgnoreCase));
                var closedDevices = group.Count(x => x.IsTemporarilyClosed);
                var devicesWithIssues = deviceIssues.Count(x => x.IssueScore > 0);
                var faultScore = deviceIssues.Sum(x => x.IssueScore)
                    + (outage?.IncidentCount ?? 0) * 4
                    + (int)Math.Round((outage?.TotalOfflineMinutes ?? 0) / 60.0);

                return new FaultDensityStoreDto
                {
                    StoreCode = storeCode,
                    StoreName = ResolveStoreName(storeCode, group.First().StoreName, storeNameMap, outage?.StoreName),
                    DeviceCount = group.Count(),
                    CurrentOfflineDevices = currentOfflineDevices,
                    CriticalDevices = criticalDevices,
                    WarningDevices = warningDevices,
                    ClosedDevices = closedDevices,
                    DevicesWithIssues = devicesWithIssues,
                    IncidentCount = outage?.IncidentCount ?? 0,
                    TotalOfflineMinutes = outage?.TotalOfflineMinutes ?? 0,
                    LastOfflineAt = outage?.LastOfflineAt,
                    FaultScore = faultScore
                };
            })
            .OrderByDescending(x => x.FaultScore)
            .ThenByDescending(x => x.CurrentOfflineDevices)
            .ThenByDescending(x => x.CriticalDevices)
            .ToList();

        return Ok(new FaultDensityReportDto
        {
            PeriodDays = days,
            GeneratedAtUtc = DateTime.UtcNow,
            Stores = storeRows,
            Devices = deviceRows.Where(x => x.IssueScore > 0).Take(100).ToList()
        });
    }

    private static List<IssueReasonDto> BuildDeviceIssueReasons(Device device)
    {
        var reasons = new List<IssueReasonDto>();

        if (!device.Online && !device.IsTemporarilyClosed)
        {
            reasons.Add(new IssueReasonDto("Offline", 12));
        }

        if (string.Equals(device.HealthStatus, "Critical", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(new IssueReasonDto("Saglik kritik", 8));
        }
        else if (string.Equals(device.HealthStatus, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(new IssueReasonDto("Saglik uyarisi", 4));
        }

        if (device.CurrentCpuUsagePercent >= 90)
        {
            reasons.Add(new IssueReasonDto("CPU %90+", 3));
        }

        if (device.CurrentRamUsagePercent >= 90)
        {
            reasons.Add(new IssueReasonDto("RAM %90+", 3));
        }

        if (device.CurrentDiskUsagePercent >= 90)
        {
            reasons.Add(new IssueReasonDto("Disk C %90+", 4));
        }

        if (device.CurrentDiskDUsagePercent.HasValue && device.CurrentDiskDUsagePercent.Value >= 90)
        {
            reasons.Add(new IssueReasonDto("Disk D %90+", 2));
        }

        return reasons;
    }

    private static int SafePercent(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
        return (int)Math.Round(Math.Clamp(value, 0, 100));
    }

    private static int? SafeNullablePercent(float? value)
    {
        if (!value.HasValue || float.IsNaN(value.Value) || float.IsInfinity(value.Value)) return null;
        return (int)Math.Round(Math.Clamp(value.Value, 0, 100));
    }

    private static string ResolveStoreName(int storeCode, string? primary, IReadOnlyDictionary<int, string> storeNameMap, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(primary)) return primary;
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
        if (storeNameMap.TryGetValue(storeCode, out var mapped) && !string.IsNullOrWhiteSpace(mapped)) return mapped;
        return storeCode > 0 ? $"Magaza {storeCode}" : "Bilinmeyen";
    }

    private sealed record IssueReasonDto(string Label, int Score);
}

public class StoreOutageReportDto
{
    public int PeriodDays { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public int TotalIncidents { get; set; }
    public int CurrentlyOfflineStoreCount { get; set; }
    public List<StoreOutageSummaryDto> Summary { get; set; } = new();
    public List<StoreOutageIncidentDto> Incidents { get; set; } = new();
}

public class StoreOutageSummaryDto
{
    public int StoreCode { get; set; }
    public string StoreName { get; set; } = "";
    public int IncidentCount { get; set; }
    public int TotalOfflineMinutes { get; set; }
    public double AverageOfflineMinutes { get; set; }
    public DateTime? LastOfflineAt { get; set; }
    public DateTime? LastOnlineAt { get; set; }
    public bool IsCurrentlyOffline { get; set; }
    public int MaxOfflineKasaCount { get; set; }
}

public class StoreOutageIncidentDto
{
    public int Id { get; set; }
    public int StoreCode { get; set; }
    public string StoreName { get; set; } = "";
    public int OfflineKasaCount { get; set; }
    public DateTime OfflineAt { get; set; }
    public DateTime? OnlineAt { get; set; }
    public int? DurationMinutes { get; set; }
    public bool IsStillOffline { get; set; }
}

public class HardwareInventoryReportDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public int TotalDevices { get; set; }
    public int OnlineDevices { get; set; }
    public int CriticalDevices { get; set; }
    public List<HardwareInventoryRowDto> Rows { get; set; } = new();
}

public class HardwareInventoryRowDto
{
    public string DeviceId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public int StoreCode { get; set; }
    public string? StoreName { get; set; }
    public string Type { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Os { get; set; } = "";
    public string? AgentVersion { get; set; }
    public string? CpuModel { get; set; }
    public string? GpuModel { get; set; }
    public long TotalRamMB { get; set; }
    public long TotalDiskGB { get; set; }
    public long? TotalDiskDGB { get; set; }
    public int CpuUsagePercent { get; set; }
    public int RamUsagePercent { get; set; }
    public int DiskUsagePercent { get; set; }
    public int? DiskDUsagePercent { get; set; }
    public string HealthStatus { get; set; } = "";
    public int HealthScore { get; set; }
    public bool Online { get; set; }
    public DateTime? LastSeen { get; set; }
    public string? LastLoggedInUser { get; set; }
    public DateTime? SystemBootTime { get; set; }
    public bool VncInstalled { get; set; }
}

public class FaultDensityReportDto
{
    public int PeriodDays { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public List<FaultDensityStoreDto> Stores { get; set; } = new();
    public List<FaultDensityDeviceDto> Devices { get; set; } = new();
}

public class FaultDensityStoreDto
{
    public int StoreCode { get; set; }
    public string StoreName { get; set; } = "";
    public int DeviceCount { get; set; }
    public int CurrentOfflineDevices { get; set; }
    public int CriticalDevices { get; set; }
    public int WarningDevices { get; set; }
    public int ClosedDevices { get; set; }
    public int DevicesWithIssues { get; set; }
    public int IncidentCount { get; set; }
    public int TotalOfflineMinutes { get; set; }
    public DateTime? LastOfflineAt { get; set; }
    public int FaultScore { get; set; }
}

public class FaultDensityDeviceDto
{
    public string DeviceId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public int StoreCode { get; set; }
    public string StoreName { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Online { get; set; }
    public bool IsTemporarilyClosed { get; set; }
    public string HealthStatus { get; set; } = "";
    public int HealthScore { get; set; }
    public int CpuUsagePercent { get; set; }
    public int RamUsagePercent { get; set; }
    public int DiskUsagePercent { get; set; }
    public DateTime? LastSeen { get; set; }
    public int IssueScore { get; set; }
    public List<string> IssueReasons { get; set; } = new();
}
