using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;
using System.Linq; 
using System; 
using System.Collections.Generic;
using System.Threading.Tasks;

// Not: Shared.Dtos'dan sadece DeviceDetailsDto/OsInfoDto kullanılıyorsa, 
// Controller'daki lokal DTO'lar (DeviceDetailDto, DeviceListDto) karışıklık yaratmaz.
// Ancak derleme hatası almamak için tüm API DTO'ları bu Controller'ın namespace'i içinde tutuldu.

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
    
    // ... (Diğer metotlar aynı kalır) ...

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

// ---------------------------------------------------------------------------------------------------
// API DTO'ları: Derleme hatalarını çözmek için Controller'ın kendi namespace'i içinde tanımlanmıştır.
// ---------------------------------------------------------------------------------------------------

// Frontend'in beklediği OS yapısını temsil eder (OsInfoDto olarak adlandırıldı)
public class OsInfoDto
{
    public string Name { get; set; } = default!; 
    public string? Version { get; set; } 
}

// ✅ YENİ DTO: Envanter listesi için
public class DeviceListDto
{
    public string Id { get; set; } = default!;
    public string? Hostname { get; set; }
    public string IpAddress { get; set; } = default!;
    public OsInfoDto Os { get; set; } = default!; 
    public int StoreCode { get; set; } 
    public string Type { get; set; } = default!; 
    public bool Online { get; set; } 
    public string? LastSeen { get; set; }
    public int? CpuUsage { get; set; } 
    public int? RamUsage { get; set; } 
    public int? DiskUsage { get; set; }
}

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

// 🏆 KRİTİK DTO: Detay Sayfası DTO'su (Metrics listesini içerir)
public class DeviceDetailDto
{
    public string Id { get; set; } = default!;
    public string? Hostname { get; set; }
    public string IpAddress { get; set; } = default!; 
    
    public OsInfoDto Os { get; set; } = default!; 
    
    public int Store { get; set; } 
    
    public string Type { get; set; } = default!;
    public bool Online { get; set; }
    public string? LastSeen { get; set; }
    
    // Metrikler
    public int? CpuUsage { get; set; }
    public int? RamUsage { get; set; }
    public int? DiskUsage { get; set; }
    
    // Yeni Eklenen Alanlar
    public string? AgentVersion { get; set; } 
    public string? SqlVersion { get; set; }
    public string? PosVersion { get; set; }
    public bool Agent { get; set; }
    
    public List<DeviceMetricDto> Metrics { get; set; } = new(); 
}

public class DeviceMetricDto
{
    public string TimestampUtc { get; set; } = default!;
    public int CpuUsagePercent { get; set; }
    public int RamUsagePercent { get; set; } 
    public int DiskUsagePercent { get; set; }
}