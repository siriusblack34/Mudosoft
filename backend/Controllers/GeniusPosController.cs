using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using System.Text.Json;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/genius-pos")]
public class GeniusPosController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly ILogger<GeniusPosController> _logger;

    public GeniusPosController(OrchestraDbContext db, ILogger<GeniusPosController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/genius-pos/stores
    /// Tüm mağazaların POS sağlık özetini döndürür.
    /// </summary>
    [HttpGet("stores")]
    public async Task<IActionResult> GetStores()
    {
        var cutoff = DateTime.UtcNow.AddHours(-2);

        // Latest GeniusPos collector reports per device
        var reports = await _db.CollectorReports
            .AsNoTracking()
            .Where(r => r.CollectorName == "GeniusPos" && r.TimestampUtc >= cutoff)
            .OrderByDescending(r => r.TimestampUtc)
            .ToListAsync();

        var latestByDevice = reports
            .GroupBy(r => r.DeviceId)
            .ToDictionary(g => g.Key, g => g.First());

        // All store devices with their agents
        var storeDevices = await _db.StoreDevices
            .AsNoTracking()
            .Where(d => d.DeviceType == "PC" || d.DeviceType.StartsWith("Kasa"))
            .OrderBy(d => d.StoreCode)
            .ThenBy(d => d.DeviceType)
            .ToListAsync();

        // Match store devices to Devices table via IP
        var allDevices = await _db.Devices
            .AsNoTracking()
            .Select(d => new { d.Id, d.IpAddress, d.Online, d.LastSeen })
            .ToListAsync();

        var ipToDevice = allDevices.ToDictionary(d => d.IpAddress, d => d);

        var storeGroups = storeDevices
            .GroupBy(sd => sd.StoreCode)
            .Select(g => new StorePosHealth
            {
                StoreCode = g.Key,
                StoreName = g.First().StoreName,
                Devices = g.Select(sd =>
                {
                    ipToDevice.TryGetValue(sd.CalculatedIpAddress, out var agent);
                    var deviceId = agent?.Id;

                    GeniusPosDataDto? posData = null;
                    if (deviceId != null && latestByDevice.TryGetValue(deviceId, out var rep))
                    {
                        posData = ParsePosData(rep);
                    }

                    return new DevicePosHealth
                    {
                        StoreDeviceId = sd.DeviceId,
                        DeviceName = sd.DeviceName,
                        DeviceType = sd.DeviceType,
                        IpAddress = sd.CalculatedIpAddress,
                        AgentDeviceId = deviceId,
                        Online = agent?.Online ?? false,
                        LastSeen = agent?.LastSeen,
                        PosData = posData,
                        HealthStatus = DetermineStatus(agent?.Online ?? false, posData)
                    };
                }).ToList()
            })
            .ToList();

        return Ok(storeGroups);
    }

    /// <summary>
    /// GET /api/genius-pos/device/{deviceId}
    /// Belirli bir agent cihazı için detaylı POS verisi döndürür.
    /// </summary>
    [HttpGet("device/{deviceId}")]
    public async Task<IActionResult> GetDevice(string deviceId)
    {
        var cutoff = DateTime.UtcNow.AddHours(-4);
        var report = await _db.CollectorReports
            .AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.CollectorName == "GeniusPos" && r.TimestampUtc >= cutoff)
            .OrderByDescending(r => r.TimestampUtc)
            .FirstOrDefaultAsync();

        if (report == null)
            return NotFound(new { error = "GeniusPos verisi bulunamadı (son 4 saat)" });

        var parsed = ParsePosData(report);
        return Ok(new
        {
            DeviceId = deviceId,
            ReportedAt = report.TimestampUtc,
            Severity = report.Severity,
            Data = parsed
        });
    }

    /// <summary>
    /// GET /api/genius-pos/store/{storeCode}
    /// Belirli mağazanın tüm kasalarının POS verisi.
    /// IP mantığı: Server=192.168.{storeCode}.2, K1=.31, K2=.32, K3=.33
    /// </summary>
    [HttpGet("store/{storeCode:int}")]
    public async Task<IActionResult> GetStore(int storeCode)
    {
        // Tüm mağaza cihazlarını al
        var storeDevices = await _db.StoreDevices
            .AsNoTracking()
            .Where(d => d.StoreCode == storeCode)
            .ToListAsync();

        if (!storeDevices.Any())
            return NotFound(new { error = $"Mağaza {storeCode} bulunamadı" });

        var ips = storeDevices.Select(d => d.CalculatedIpAddress).ToList();
        var agents = await _db.Devices
            .AsNoTracking()
            .Where(d => ips.Contains(d.IpAddress))
            .ToListAsync();

        var ipToAgent = agents.ToDictionary(d => d.IpAddress);

        var cutoff = DateTime.UtcNow.AddHours(-4);
        var agentIds = agents.Select(a => a.Id).ToList();
        var reports = await _db.CollectorReports
            .AsNoTracking()
            .Where(r => agentIds.Contains(r.DeviceId) && r.CollectorName == "GeniusPos" && r.TimestampUtc >= cutoff)
            .OrderByDescending(r => r.TimestampUtc)
            .ToListAsync();

        var latestByAgent = reports
            .GroupBy(r => r.DeviceId)
            .ToDictionary(g => g.Key, g => g.First());

        var result = storeDevices.Select(sd =>
        {
            ipToAgent.TryGetValue(sd.CalculatedIpAddress, out var agent);
            var deviceId = agent?.Id;

            GeniusPosDataDto? posData = null;
            if (deviceId != null && latestByAgent.TryGetValue(deviceId, out var rep))
                posData = ParsePosData(rep);

            return new DevicePosHealth
            {
                StoreDeviceId = sd.DeviceId,
                DeviceName = sd.DeviceName,
                DeviceType = sd.DeviceType,
                IpAddress = sd.CalculatedIpAddress,
                AgentDeviceId = deviceId,
                Online = agent?.Online ?? false,
                LastSeen = agent?.LastSeen,
                PosData = posData,
                HealthStatus = DetermineStatus(agent?.Online ?? false, posData)
            };
        }).ToList();

        return Ok(new
        {
            StoreCode = storeCode,
            StoreName = storeDevices.First().StoreName,
            Devices = result
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static GeniusPosDataDto? ParsePosData(CollectorReport report)
    {
        try
        {
            return JsonSerializer.Deserialize<GeniusPosDataDto>(report.JsonData,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static string DetermineStatus(bool online, GeniusPosDataDto? pos)
    {
        if (!online) return "Offline";
        if (pos == null) return "Unknown";
        if (!pos.DbConnectable) return "Critical";
        if (pos.Services.Any(s => s.Status == "Stopped")) return "Critical";
        if (pos.StockTransferErrorCount > 100 || pos.ExportErrLogCount > 50) return "Warning";
        if (pos.SeqFileCount > 500 || pos.SeqXmlTotalMB > 500) return "Warning";
        return "Healthy";
    }
}

// ── Response DTOs ─────────────────────────────────────────────────────────

public class StorePosHealth
{
    public int StoreCode { get; set; }
    public string? StoreName { get; set; }
    public List<DevicePosHealth> Devices { get; set; } = new();
}

public class DevicePosHealth
{
    public string StoreDeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string? AgentDeviceId { get; set; }
    public bool Online { get; set; }
    public DateTime? LastSeen { get; set; }
    public GeniusPosDataDto? PosData { get; set; }
    public string HealthStatus { get; set; } = "Unknown";
}

public class GeniusPosDataDto
{
    public DateTime CollectedAt { get; set; }
    public List<PosServiceStatusDto> Services { get; set; } = new();
    public string? JreHome { get; set; }
    public string? PosVersion { get; set; }
    public string? SqlVersion { get; set; }
    public bool DbConnectable { get; set; }
    public int StockTransferErrorCount { get; set; }
    public int ExportErrLogCount { get; set; }
    public DateTime? LastSuccessfulTransferAt { get; set; }
    public int SeqFileCount { get; set; }
    public int XmlFileCount { get; set; }
    public double SeqXmlTotalMB { get; set; }
    public string? PosDataPath { get; set; }
}

public class PosServiceStatusDto
{
    public string ServiceName { get; set; } = "";
    public string Status { get; set; } = "";
}
