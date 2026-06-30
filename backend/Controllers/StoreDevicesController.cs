using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/store-devices")]
public class StoreDevicesController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly ILogger<StoreDevicesController> _logger;

    public StoreDevicesController(OrchestraDbContext db, ILogger<StoreDevicesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _db.StoreDevices
            .OrderBy(d => d.StoreCode).ThenBy(d => d.DeviceType)
            // 🔒 SECURITY: DbConnectionString (içinde sa parolası var) artık liste endpoint'inde DÖNMÜYOR.
            // Her kimlikli kullanıcı (Teknisyen dahil) tüm mağazaların SQL kimlik bilgisini çekebiliyordu.
            // Backend SQL sorgularını deviceId'den kendisi çözer; admin düzenleme formu boş bırakıldığında
            // mevcut connection string Update'te korunur (IsNullOrWhiteSpace guard).
            .Select(d => new
            {
                d.DeviceId, d.StoreCode, d.StoreName, d.DeviceType,
                d.DeviceName, d.CalculatedIpAddress,
                d.IsTemporarilyClosed, d.TemporaryCloseReason, d.LastSeen
            })
            .ToListAsync();
        return Ok(devices);
    }

    // ===========================================================
    // MAĞAZA SEVİYESİ PASİF/KAPALI — tadilat/planlı kesinti için
    // Mağazanın TÜM cihazlarını "geçici kapalı" yapar; taramaya girmez,
    // offline tespiti/alarmı çalmaz, listede "Kapalı" görünür.
    // ===========================================================
    [HttpPut("{storeCode:int}/temporary-close")]
    public async Task<IActionResult> ToggleStoreTemporaryClose(int storeCode, [FromBody] StoreTemporaryCloseRequest req)
    {
        var devices = await _db.StoreDevices.Where(d => d.StoreCode == storeCode).ToListAsync();
        if (devices.Count == 0)
            return NotFound(new { error = "Mağazaya ait cihaz bulunamadı" });

        var storeName = devices[0].StoreName;
        var reason = req.IsClosed ? (req.Reason?.Trim() ?? "") : null;

        foreach (var d in devices)
        {
            d.IsTemporarilyClosed = req.IsClosed;
            d.TemporaryCloseReason = reason;
        }
        await _db.SaveChangesAsync();

        // Cache'i anında güncelle (with-status cache'ten okuyor; dashboard yanıp sönmesin)
        foreach (var d in devices)
        {
            DeviceStatusWorker.UpdateCachedDevice(d.DeviceId, c =>
            {
                c.IsTemporarilyClosed = req.IsClosed;
                c.TemporaryCloseReason = reason;
            });
        }

        _logger.LogInformation("Store {StoreCode} ({StoreName}) temporary-close={IsClosed}, {Count} cihaz, reason: {Reason}",
            storeCode, storeName, req.IsClosed, devices.Count, reason);

        return Ok(new
        {
            success = true,
            storeCode,
            storeName,
            affectedDevices = devices.Count,
            isStoreClosed = req.IsClosed,
            message = req.IsClosed
                ? $"Mağaza {storeCode} ({storeName}) ve {devices.Count} cihazı kapalı olarak işaretlendi"
                : $"Mağaza {storeCode} ({storeName}) yeniden aktif edildi"
        });
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

    /// <summary>
    /// Yeni mağaza prosedürü — tek seferde Router + PC + N kasa oluşturur.
    /// IP bloğu: {ipBlock}.1 (Router), {ipBlock}.2 (PC), {ipBlock}.31..3N (Kasalar)
    /// </summary>
    [HttpPost("provision")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Provision([FromBody] ProvisionStoreRequest req)
    {
        if (req.StoreCode <= 0) return BadRequest(new { error = "Mağaza kodu gerekli" });
        if (string.IsNullOrWhiteSpace(req.StoreName)) return BadRequest(new { error = "Mağaza adı gerekli" });
        if (req.KasaCount < 0 || req.KasaCount > 10) return BadRequest(new { error = "Kasa sayısı 0-10 arası olmalı" });

        // IP bloğu: belirtildiyse onu kullan, yoksa 192.168.{storeCode}
        var ipBlock = string.IsNullOrWhiteSpace(req.IpBlock) ? $"192.168.{req.StoreCode}" : req.IpBlock.Trim().TrimEnd('.');
        var name = req.StoreName.Trim();
        var code = req.StoreCode;

        // Zaten var mı kontrolü
        if (await _db.StoreDevices.AnyAsync(d => d.StoreCode == code))
            return BadRequest(new { error = $"Mağaza {code} zaten kayıtlı. Önce mevcut cihazları silin." });

        // DB connection string builder
        var geniusDbUser = Environment.GetEnvironmentVariable("GENIUS_DB_USER") ?? "sa";
        var geniusDbPass = Environment.GetEnvironmentVariable("GENIUS_DB_PASS") ?? "";
        string BuildConn(string ip) =>
            string.IsNullOrWhiteSpace(geniusDbPass)
                ? ""
                : $"Server={ip};Database=Genius3;User Id={geniusDbUser};Password={geniusDbPass};TrustServerCertificate=True;Connect Timeout=30;";

        var devices = new List<StoreDevice>();

        // Router
        devices.Add(new StoreDevice
        {
            DeviceId = $"{code}-Router",
            StoreCode = code, StoreName = name,
            DeviceType = "Router", DeviceName = $"{code}-Router",
            CalculatedIpAddress = $"{ipBlock}.1",
            DbConnectionString = "",
        });

        // PC
        var pcIp = $"{ipBlock}.2";
        devices.Add(new StoreDevice
        {
            DeviceId = $"{code}-PC",
            StoreCode = code, StoreName = name,
            DeviceType = "PC", DeviceName = $"{code}-PC",
            CalculatedIpAddress = pcIp,
            DbConnectionString = BuildConn(pcIp),
        });

        // Kasalar: .31, .32, .33, ...
        for (int i = 1; i <= req.KasaCount; i++)
        {
            var kasaIp = $"{ipBlock}.{30 + i}";
            devices.Add(new StoreDevice
            {
                DeviceId = $"{code}-K{i}",
                StoreCode = code, StoreName = name,
                DeviceType = $"Kasa-{i}", DeviceName = $"{code}-Kasa-{i}",
                CalculatedIpAddress = kasaIp,
                DbConnectionString = BuildConn(kasaIp),
            });

            // Yazıcılar: .41, .42, .43, ...
            var yaziciIp = $"{ipBlock}.{40 + i}";
            devices.Add(new StoreDevice
            {
                DeviceId = $"{code}-Y{i}",
                StoreCode = code, StoreName = name,
                DeviceType = $"Yazici-{i}", DeviceName = $"{code}-Yazici-{i}",
                CalculatedIpAddress = yaziciIp,
                DbConnectionString = "",
            });
        }

        _db.StoreDevices.AddRange(devices);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Store provisioned: {Code} {Name} — {Count} devices (IP block: {IpBlock})",
            code, name, devices.Count, ipBlock);

        return Ok(new
        {
            success = true,
            storeCode = code,
            storeName = name,
            ipBlock,
            devicesCreated = devices.Select(d => new { d.DeviceId, d.DeviceType, d.CalculatedIpAddress }).ToList(),
        });
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

    /// <summary>
    /// Mevcut tüm Kasa cihazlarına ait Yazici cihazlarını toplu olarak oluşturur.
    /// IP: Kasa IP'nin son oktetini +10 yaparak türetir (31→41, 32→42, 33→43).
    /// İdempotent: zaten var olan yazıcıları atlar.
    /// </summary>
    [HttpPost("provision-printers")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ProvisionPrinters()
    {
        var kasalar = await _db.StoreDevices
            .Where(d => d.DeviceType.StartsWith("Kasa-"))
            .AsNoTracking()
            .ToListAsync();

        var existingYaziciIds = (await _db.StoreDevices
            .Where(d => d.DeviceType.StartsWith("Yazici-"))
            .Select(d => d.DeviceId)
            .ToListAsync())
            .ToHashSet();

        var toAdd = new List<StoreDevice>();

        foreach (var kasa in kasalar)
        {
            var dash = kasa.DeviceType.LastIndexOf('-');
            if (dash < 0 || !int.TryParse(kasa.DeviceType.AsSpan(dash + 1), out var kasaNum)) continue;

            var yaziciId = $"{kasa.StoreCode}-Y{kasaNum}";
            if (existingYaziciIds.Contains(yaziciId)) continue;

            var parts = kasa.CalculatedIpAddress.Split('.');
            if (parts.Length != 4) continue;
            var yaziciIp = $"{parts[0]}.{parts[1]}.{parts[2]}.{40 + kasaNum}";

            toAdd.Add(new StoreDevice
            {
                DeviceId = yaziciId,
                StoreCode = kasa.StoreCode,
                StoreName = kasa.StoreName,
                DeviceType = $"Yazici-{kasaNum}",
                DeviceName = $"{kasa.StoreCode}-Yazici-{kasaNum}",
                CalculatedIpAddress = yaziciIp,
                DbConnectionString = "",
            });
        }

        if (toAdd.Count > 0)
        {
            _db.StoreDevices.AddRange(toAdd);
            await _db.SaveChangesAsync();
            Orchestra.Backend.Services.DeviceStatusWorker.InvalidateCache();
        }

        _logger.LogInformation("ProvisionPrinters: added {Added}, skipped {Skipped}", toAdd.Count, kasalar.Count - toAdd.Count);
        return Ok(new { added = toAdd.Count, skipped = kasalar.Count - toAdd.Count, total = kasalar.Count });
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
        DeviceStatusWorker.RemoveFromCache(deviceId);
        return Ok(new { success = true, deletedDeviceId = deviceId });
    }
}

public class ProvisionStoreRequest
{
    public int StoreCode { get; set; }
    public string StoreName { get; set; } = "";
    /// <summary>IP bloğu prefix: ör. "192.168.240". Boşsa "192.168.{StoreCode}" kullanılır.</summary>
    public string? IpBlock { get; set; }
    /// <summary>Kasa sayısı (0-10). Default 3.</summary>
    public int KasaCount { get; set; } = 3;
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

public class StoreTemporaryCloseRequest
{
    public bool IsClosed { get; set; }
    public string? Reason { get; set; }
}
