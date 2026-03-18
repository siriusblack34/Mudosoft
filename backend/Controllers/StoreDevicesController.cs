using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/store-devices")]
public class StoreDevicesController : ControllerBase
{
    private readonly MudoSoftDbContext _db;
    private readonly ILogger<StoreDevicesController> _logger;

    public StoreDevicesController(MudoSoftDbContext db, ILogger<StoreDevicesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _db.StoreDevices
            .OrderBy(d => d.StoreCode).ThenBy(d => d.DeviceType)
            .Select(d => new
            {
                d.DeviceId, d.StoreCode, d.StoreName, d.DeviceType,
                d.DeviceName, d.CalculatedIpAddress, d.DbConnectionString,
                d.IsTemporarilyClosed, d.TemporaryCloseReason, d.LastSeen
            })
            .ToListAsync();
        return Ok(devices);
    }

    [HttpGet("stores")]
    public async Task<IActionResult> GetStores()
    {
        var stores = await _db.StoreDevices
            .GroupBy(d => new { d.StoreCode, d.StoreName })
            .Select(g => new { g.Key.StoreCode, g.Key.StoreName, DeviceCount = g.Count() })
            .OrderBy(s => s.StoreCode)
            .ToListAsync();
        return Ok(stores);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateStoreDeviceRequest req)
    {
        if (req.StoreCode <= 0 || string.IsNullOrWhiteSpace(req.StoreName) || string.IsNullOrWhiteSpace(req.DeviceType))
            return BadRequest(new { error = "Mağaza kodu, adı ve cihaz tipi gerekli" });

        var deviceId = $"{req.StoreCode}-{req.DeviceType}-{req.DeviceName ?? req.DeviceType}".Trim();

        if (await _db.StoreDevices.AnyAsync(d => d.DeviceId == deviceId))
            return BadRequest(new { error = $"Bu cihaz ID zaten mevcut: {deviceId}" });

        // IP hesapla: 10.0.{storeCode}.X
        var ipAddress = req.CalculatedIpAddress;
        if (string.IsNullOrWhiteSpace(ipAddress))
            ipAddress = $"10.0.{req.StoreCode}.0";

        // DB connection string
        var connStr = req.DbConnectionString;
        if (string.IsNullOrWhiteSpace(connStr))
            connStr = $"Server={ipAddress};Database=PARAPOS;User Id=sa;Password=MudoPOS2024!;TrustServerCertificate=true;Connection Timeout=5;";

        var device = new StoreDevice
        {
            DeviceId = deviceId,
            StoreCode = req.StoreCode,
            StoreName = req.StoreName.Trim(),
            DeviceType = req.DeviceType.Trim(),
            DeviceName = req.DeviceName?.Trim() ?? req.DeviceType.Trim(),
            CalculatedIpAddress = ipAddress,
            DbConnectionString = connStr,
            CreatedDate = DateTimeOffset.UtcNow,
            LastSyncDate = DateTimeOffset.UtcNow
        };

        _db.StoreDevices.Add(device);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Store device created: {DeviceId} (Store {StoreCode})", deviceId, req.StoreCode);
        return Ok(new { success = true, device.DeviceId, device.StoreCode, device.StoreName });
    }

    [HttpPut("{deviceId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(string deviceId, [FromBody] UpdateStoreDeviceRequest req)
    {
        var device = await _db.StoreDevices.FindAsync(deviceId);
        if (device == null) return NotFound(new { error = "Cihaz bulunamadı" });

        if (!string.IsNullOrWhiteSpace(req.StoreName)) device.StoreName = req.StoreName.Trim();
        if (!string.IsNullOrWhiteSpace(req.DeviceName)) device.DeviceName = req.DeviceName.Trim();
        if (!string.IsNullOrWhiteSpace(req.DeviceType)) device.DeviceType = req.DeviceType.Trim();
        if (!string.IsNullOrWhiteSpace(req.CalculatedIpAddress)) device.CalculatedIpAddress = req.CalculatedIpAddress.Trim();
        if (!string.IsNullOrWhiteSpace(req.DbConnectionString)) device.DbConnectionString = req.DbConnectionString.Trim();

        device.LastSyncDate = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Store device updated: {DeviceId}", deviceId);
        return Ok(new { success = true, device.DeviceId });
    }

    [HttpDelete("{deviceId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string deviceId)
    {
        var device = await _db.StoreDevices.FindAsync(deviceId);
        if (device == null) return NotFound(new { error = "Cihaz bulunamadı" });

        _db.StoreDevices.Remove(device);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Store device deleted: {DeviceId}", deviceId);
        return Ok(new { success = true, deletedDeviceId = deviceId });
    }
}

public class CreateStoreDeviceRequest
{
    public int StoreCode { get; set; }
    public string StoreName { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public string? DeviceName { get; set; }
    public string? CalculatedIpAddress { get; set; }
    public string? DbConnectionString { get; set; }
}

public class UpdateStoreDeviceRequest
{
    public string? StoreName { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceType { get; set; }
    public string? CalculatedIpAddress { get; set; }
    public string? DbConnectionString { get; set; }
}
