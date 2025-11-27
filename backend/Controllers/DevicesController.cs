using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceRepository _repo;
    private readonly MudoSoftDbContext _dbContext;

    public DevicesController(IDeviceRepository repo, MudoSoftDbContext dbContext)
    {
        _repo = repo;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Dashboard statistics: online/offline counts & recent offline list
    /// GET: /api/devices/status
    /// </summary>
    [HttpGet("status")]
    public ActionResult<DashboardDto> GetStatus()
    {
        var devices = _repo.GetAll() ?? new List<Device>();

        var total = devices.Count;
        var online = devices.Count(d => d.Online);
        var offline = total - online;

        var recentOffline = devices
            .Where(d => !d.Online)
            .OrderByDescending(d => d.LastSeen)
            .Take(10)
            .Select(d => new RecentOfflineDevice
            {
                Hostname = d.Hostname ?? "-",
                Ip = d.IpAddress,
                Os = d.Os ?? "-",
                Store = d.StoreCode,
                LastSeen = d.LastSeen?.ToString("g") ?? "-"
            })
            .ToList();

        return Ok(new DashboardDto
        {
            TotalDevices = total,
            Online = online,
            Offline = offline,
            RecentOffline = recentOffline
        });
    }

    /// <summary>
    /// Full devices inventory list
    /// GET: /api/devices/inventory
    /// </summary>
    [HttpGet("inventory")]
    public ActionResult<IEnumerable<Device>> GetInventory()
    {
        return Ok(_repo.GetAll());
    }

    /// <summary>
    /// Returns a device by ID WITH last 24h metrics
    /// GET: /api/devices/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<DeviceDetailDto>> GetById(string id)
    {
        var device = _repo.GetById(id);
        if (device is null)
            return NotFound($"Device not found: {id}");

        var last24Hours = DateTime.UtcNow.AddHours(-24);

        var metrics = await _dbContext.DeviceMetrics
            .Where(m => m.DeviceId == id && m.TimestampUtc >= last24Hours)
            .OrderBy(m => m.TimestampUtc)
            .Select(m => new DeviceMetricDto
            {
                TimestampUtc = m.TimestampUtc.ToString("o"), // ISO 8601
                CpuUsagePercent = m.CpuUsagePercent,
                RamUsagePercent = m.RamUsagePercent,
                DiskUsagePercent = m.DiskUsagePercent
            })
            .ToListAsync();

        return Ok(new DeviceDetailDto
        {
            Id = device.Id,
            Hostname = device.Hostname,
            IpAddress = device.IpAddress,
            Os = device.Os,
            StoreCode = device.StoreCode,
            Type = device.Type.ToString(),
            Online = device.Online,
            LastSeen = device.LastSeen?.ToString("o"),
            CpuUsage = metrics.LastOrDefault()?.CpuUsagePercent,
            RamUsage = metrics.LastOrDefault()?.RamUsagePercent,
            DiskUsage = metrics.LastOrDefault()?.DiskUsagePercent,
            Metrics = metrics
        });
    }

    /// <summary>
    /// Returns ONLY device metrics for the last 24 hours
    /// GET: /api/devices/{id}/metrics
    /// </summary>
    [HttpGet("{id}/metrics")]
    public async Task<ActionResult<IEnumerable<DeviceMetricDto>>> GetDeviceMetrics(string id)
    {
        var device = _repo.GetById(id);
        if (device == null)
            return NotFound($"Device not found: {id}");

        var last24Hours = DateTime.UtcNow.AddHours(-24);

        var metrics = await _dbContext.DeviceMetrics
            .Where(m => m.DeviceId == id && m.TimestampUtc >= last24Hours)
            .OrderBy(m => m.TimestampUtc)
            .Select(m => new DeviceMetricDto
            {
                TimestampUtc = m.TimestampUtc.ToString("o"),
                CpuUsagePercent = m.CpuUsagePercent,
                RamUsagePercent = m.RamUsagePercent,
                DiskUsagePercent = m.DiskUsagePercent
            })
            .ToListAsync();

        return Ok(metrics);
    }
}

// DTO'lar
public class DashboardDto
{
    public int TotalDevices { get; set; }
    public int Online { get; set; }
    public int Offline { get; set; }
    public List<RecentOfflineDevice> RecentOffline { get; set; } = new();
}

public class RecentOfflineDevice
{
    public string Hostname { get; set; } = default!;
    public string Ip { get; set; } = default!;
    public string Os { get; set; } = default!;
    public int Store { get; set; }
    public string LastSeen { get; set; } = default!;
}

public class DeviceDetailDto
{
    public string Id { get; set; } = default!;
    public string? Hostname { get; set; }
    public string IpAddress { get; set; } = default!;
    public string? Os { get; set; }
    public int StoreCode { get; set; }
    public string Type { get; set; } = default!;
    public bool Online { get; set; }
    public string? LastSeen { get; set; }
    public int? CpuUsage { get; set; }
    public int? RamUsage { get; set; }
    public int? DiskUsage { get; set; }
    public List<DeviceMetricDto> Metrics { get; set; } = new();
}

public class DeviceMetricDto
{
    public string TimestampUtc { get; set; } = default!;
    public int CpuUsagePercent { get; set; }
    public int RamUsagePercent { get; set; }
    public int DiskUsagePercent { get; set; }
}