using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;
using System.Linq; // Linq uzantıları için

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
    // ✅ GÜNCELLEME: En güvenli DTO eşlemesi eklendi.
    [HttpGet("inventory")]
    public ActionResult<IEnumerable<DeviceListDto>> GetInventory()
    {
        var devices = _repo.GetAll();
        
        // Yardımcı fonksiyon: Float değerini güvenli bir şekilde int?'ye dönüştürür.
        int? SafeRoundToNullableInt(float rawValue)
        {
            // NaN (Sayı Değil) veya sonsuzluk olup olmadığını kontrol et (Casting hatasını önler)
            if (float.IsNaN(rawValue) || float.IsInfinity(rawValue))
            {
                return null;
            }
            
            // Yuvarla ve 0'dan büyükse döndür (DTO'nun nullability'sini korur)
            var roundedValue = (int)Math.Round(rawValue);
            return roundedValue > 0 ? (int?)roundedValue : null;
        }
    
        var deviceDtos = devices.Select(d => 
        {
            return new DeviceListDto
            {
                Id = d.Id,
                Hostname = d.Hostname,
                IpAddress = d.IpAddress,
                Os = new OsInfo { Name = d.Os ?? "-" }, 
                StoreCode = d.StoreCode, 
                Type = d.Type.ToString(), 
                Online = d.Online, 
                LastSeen = d.LastSeen?.ToString("o"),
                
                // 🟢 Metrikler: Güvenli fonksiyondan değer atanır.
                CpuUsage = SafeRoundToNullableInt(d.CurrentCpuUsagePercent),
                RamUsage = SafeRoundToNullableInt(d.CurrentRamUsagePercent),
                DiskUsage = SafeRoundToNullableInt(d.CurrentDiskUsagePercent)
            };
        }).ToList();

        return Ok(deviceDtos);
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
            Os = device.Os, // String olarak kalır
            StoreCode = device.StoreCode,
            Type = device.Type.ToString(),
            Online = device.Online,
            LastSeen = device.LastSeen?.ToString("o"),
            // ✅ Metrikler: Current* alanlarından alınıyor.
            CpuUsage = (int)Math.Round(device.CurrentCpuUsagePercent),
            RamUsage = (int)Math.Round(device.CurrentRamUsagePercent),
            DiskUsage = (int)Math.Round(device.CurrentDiskUsagePercent),
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

// ✅ YENİ DTO: Envanter listesi için (Frontend'e özel alanlar içerir)
public class DeviceListDto
{
    public string Id { get; set; } = default!;
    public string? Hostname { get; set; }
    public string IpAddress { get; set; } = default!;
    // Front-end'in DeviceList.tsx dosyasındaki device.os.name kullanımını desteklemek için OsInfo DTO'su eklendi.
    public OsInfo Os { get; set; } = default!; 
    public int StoreCode { get; set; } // ✅ Store
    public string Type { get; set; } = default!; // ✅ Type
    public bool Online { get; set; } // Status bilgisini Online olarak kullanıyoruz.
    public string? LastSeen { get; set; }

    // Front-end'in beklediği alan adları.
    public int? CpuUsage { get; set; } // ✅ CPU
    public int? RamUsage { get; set; } // ✅ RAM
    public int? DiskUsage { get; set; }
}

// Front-end'in beklediği OS yapısını temsil eder
public class OsInfo
{
    public string Name { get; set; } = default!;
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
    public int RamUsagePercent { get; set; } // ✅ DÜZELTME: get ve set arasında noktalı virgül eklendi
    public int DiskUsagePercent { get; set; }
}