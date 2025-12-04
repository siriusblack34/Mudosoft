using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;
using System.Linq; 
using System; 
using System.Collections.Generic;
using System.Threading.Tasks;

// HATA GİDERME: Eksik DTO'ların bulunduğu Mudosoft.Shared.Dtos ad alanı eklendi.
using Mudosoft.Shared.Dtos; 


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

    // Yardımcı fonksiyon: Float değerini güvenli bir şekilde int?'ye dönüştürür.
    private int? SafeRoundToNullableInt(float rawValue)
    {
        if (float.IsNaN(rawValue) || float.IsInfinity(rawValue))
        {   
            return null;
        }
        var roundedValue = (int)Math.Round(rawValue);
        return roundedValue > 0 ? (int?)roundedValue : null;
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
    public ActionResult<IEnumerable<DeviceListDto>> GetInventory()
    {
        var devices = _repo.GetAll();
        
        var deviceDtos = devices.Select(d => 
        {
            var osInfoLocal = new OsInfoDto { Name = d.Os ?? "-" };

            return new DeviceListDto
            {
                Id = d.Id,
                Hostname = d.Hostname,
                IpAddress = d.IpAddress,
                Os = osInfoLocal, 
                StoreCode = d.StoreCode, 
                Type = d.Type.ToString(), 
                Online = d.Online, 
                LastSeen = d.LastSeen?.ToString("o"),
                
                CpuUsage = SafeRoundToNullableInt(d.CurrentCpuUsagePercent),
                RamUsage = SafeRoundToNullableInt(d.CurrentRamUsagePercent),
                DiskUsage = SafeRoundToNullableInt(d.CurrentDiskUsagePercent)
            };
        }).ToList();

        return Ok(deviceDtos);
    }

    /// <summary>
    /// Returns a device by ID WITH LIMITED last 24h metrics for graph display
    /// GET: /api/devices/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<DeviceDetailDto>> GetById(string id)
    {
        var device = _repo.GetById(id);
        if (device is null)
            return NotFound($"Device not found: {id}");

        var last24Hours = DateTime.UtcNow.AddHours(-24);

        // 🚀 OPTİMİZASYON: Sadece son 120 kaydı çekerek network yükü azaltıldı.
        var metrics = await _dbContext.DeviceMetrics
            .Where(m => m.DeviceId == id && m.TimestampUtc >= last24Hours)
            .OrderByDescending(m => m.TimestampUtc) 
            .Take(120) 
            .OrderBy(m => m.TimestampUtc)
            .Select(m => new DeviceMetricDto
            {
                TimestampUtc = m.TimestampUtc.ToString("o"), 
                CpuUsagePercent = m.CpuUsagePercent,
                RamUsagePercent = m.RamUsagePercent, // Düzeltildi
                DiskUsagePercent = m.DiskUsagePercent
            })
            .ToListAsync();

        // OS string'ini OsInfoDto nesnesine dönüştürme mantığı
        var osInfoLocal = new OsInfoDto(); 
        if (!string.IsNullOrWhiteSpace(device.Os))
        {
            var osString = device.Os;
            var firstSpaceIndex = osString.IndexOf(' ');
            
            if (firstSpaceIndex > 0)
            {
                osInfoLocal.Name = osString.Substring(0, firstSpaceIndex); 
                osInfoLocal.Version = osString.Substring(firstSpaceIndex).Trim();
            }
            else
            {
                osInfoLocal.Name = osString; 
                osInfoLocal.Version = "-";
            }
        } else {
             osInfoLocal.Name = "Unknown"; 
             osInfoLocal.Version = "-";
        }
        
        return Ok(new DeviceDetailDto
        {
            Id = device.Id,
            Hostname = device.Hostname,
            IpAddress = device.IpAddress, 
            
            Os = osInfoLocal, 
            
            Store = device.StoreCode, 
            AgentVersion = device.AgentVersion, 
            
            Type = device.Type.ToString(),
            Online = device.Online,
            LastSeen = device.LastSeen?.ToString("o"),
            
            // Metrikler
            CpuUsage = (int)Math.Round(device.CurrentCpuUsagePercent),
            RamUsage = (int)Math.Round(device.CurrentRamUsagePercent),
            DiskUsage = (int)Math.Round(device.CurrentDiskUsagePercent),
            
            SqlVersion = device.SqlVersion,
            PosVersion = device.PosVersion,
            Agent = !string.IsNullOrEmpty(device.AgentVersion),
            
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