using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;
using System.Data;

namespace MudoSoft.Backend.Controllers
{
    [ApiController]
    [Authorize] // 🔒 Authentication required
    [Route("api/sqlquery")]
    public class SqlQueryController : ControllerBase
    {
        private readonly IRemoteSqlService _remoteSqlService;
        private readonly MudoSoftDbContext _db;
        private readonly ILogger<SqlQueryController> _logger;
        private readonly FastSqlReachabilityService _fastCheck;

        // Cache artık DeviceStatusWorker tarafından yönetiliyor

        public SqlQueryController(
            IRemoteSqlService remoteSqlService,
            MudoSoftDbContext db,
            FastSqlReachabilityService fastCheck,
            ILogger<SqlQueryController> logger)
        {
            _remoteSqlService = remoteSqlService;
            _db = db;
            _fastCheck = fastCheck;
            _logger = logger;
        }

        public static List<StoreDeviceWithStatusDto>? GetCachedDevices()
        {
            return DeviceStatusWorker.GetCachedDevices();
        }

        // ===========================================================
        // 0) TÜM CİHAZLAR (ENVANTER)
        // ===========================================================
        [HttpGet("devices/all")]
        public async Task<IActionResult> GetAllDevices()
        {
            var devices = await _db.StoreDevices
                .AsNoTracking()
                .Select(d => new
                {
                    d.DeviceId,
                    d.StoreCode,
                    d.StoreName,
                    d.DeviceType,
                    d.CalculatedIpAddress
                })
                .ToListAsync();

            return Ok(devices);
        }

        // ===========================================================
        // 1) TÜM CİHAZLAR + ONLINE / OFFLINE (TEK DOĞRU ENDPOINT)
        //    Ağır tarama DeviceStatusWorker'da arka planda yapılır.
        //    Bu endpoint sadece cache'den döner — anında yanıt.
        // ===========================================================
        [HttpGet("devices/with-status")]
        public async Task<IActionResult> GetDevicesWithStatus(
            [FromQuery] int timeoutMs = 500,
            [FromQuery] int maxConcurrency = 40)
        {
            // 1) Worker cache'inden dön (anında)
            var cached = DeviceStatusWorker.GetCachedDevices();
            if (cached != null)
            {
                return Ok(cached);
            }

            // 2) İlk açılış — worker henüz tarama yapmamış, DB'den son bilinen durumu döndür
            _logger.LogInformation("DeviceStatusWorker cache boş, DB'den fallback döndürülüyor");
            var devices = await _db.StoreDevices
                .AsNoTracking()
                .Select(d => new StoreDeviceWithStatusDto
                {
                    DeviceId = d.DeviceId,
                    StoreCode = d.StoreCode,
                    StoreName = d.StoreName,
                    DeviceType = d.DeviceType,
                    DeviceName = d.DeviceName,
                    CalculatedIpAddress = d.CalculatedIpAddress,
                    IsOnline = false, // henüz taranmadı, güvenli varsayım
                    LastSeen = d.LastSeen,
                    IsTemporarilyClosed = d.IsTemporarilyClosed,
                    TemporaryCloseReason = d.TemporaryCloseReason
                })
                .ToListAsync();

            return Ok(devices);
        }

        // ===========================================================
        // 1.6) OFFLİNE LOG SORGULAMA
        // ===========================================================
        [HttpGet("offline-logs")]
        public async Task<IActionResult> GetOfflineLogs(
            [FromQuery] int days = 7,
            [FromQuery] int? storeCode = null)
        {
            var since = DateTime.UtcNow.AddDays(-days);

            var query = _db.StoreOfflineLogs
                .AsNoTracking()
                .Where(l => l.OfflineAt >= since);

            if (storeCode.HasValue)
                query = query.Where(l => l.StoreCode == storeCode.Value);

            var logs = await query
                .OrderByDescending(l => l.OfflineAt)
                .Select(l => new
                {
                    l.Id,
                    l.StoreCode,
                    l.StoreName,
                    l.OfflineKasaCount,
                    l.OfflineAt,
                    l.OnlineAt,
                    l.DurationMinutes,
                    IsStillOffline = l.OnlineAt == null
                })
                .ToListAsync();

            return Ok(logs);
        }

        // ===========================================================
        // 1.7) OFFLİNE LOG İSTATİSTİK (en çok sorun yaşayan mağazalar)
        // ===========================================================
        [HttpGet("offline-logs/stats")]
        public async Task<IActionResult> GetOfflineStats([FromQuery] int days = 30)
        {
            var since = DateTime.UtcNow.AddDays(-days);

            var stats = await _db.StoreOfflineLogs
                .AsNoTracking()
                .Where(l => l.OfflineAt >= since)
                .GroupBy(l => new { l.StoreCode, l.StoreName })
                .Select(g => new
                {
                    g.Key.StoreCode,
                    g.Key.StoreName,
                    TotalIncidents = g.Count(),
                    TotalOfflineMinutes = g.Sum(l => l.DurationMinutes ?? 0),
                    LastOfflineAt = g.Max(l => l.OfflineAt),
                    IsCurrentlyOffline = g.Any(l => l.OnlineAt == null)
                })
                .OrderByDescending(s => s.TotalIncidents)
                .ToListAsync();

            return Ok(stats);
        }

        // ===========================================================
        // 2) SQL SORGUSU ÇALIŞTIR (🔒 WHITELIST + VALIDATION)
        // ===========================================================
        
        // 🔒 İzin verilen SQL sorgu başlangıçları (whitelist)
        private static readonly string[] AllowedQueryPrefixes = new[]
        {
            "SELECT TOP",
            "SELECT COUNT",
            "SELECT SUM",
            "SELECT AVG",
            "SELECT DISTINCT",
            "SELECT *",  // Genel SELECT sorgularına izin ver
            "UPDATE",    // UPDATE sorgularına izin ver
            "INSERT",    // INSERT sorgularına izin ver
            "DELETE",    // DELETE sorgularına izin ver
            "TRUNCATE",  // TRUNCATE sorgularına izin ver
            "CREATE",    // CREATE sorgularına izin ver
            "ALTER",     // ALTER sorgularına izin ver
            "DROP"       // DROP sorgularına izin ver
        };

        // 🔒 İzin verilen özel komutlar (tam eşleşme)
        private static readonly string[] AllowedSpecialCommands = new[]
        {
            "TRUNCATE TABLE POS_STOCK_TRANSFER"  // Stok transfer tablosu temizleme
        };

        // 🔒 Tehlikeli SQL anahtar kelimeleri (blacklist)
        private static readonly string[] DangerousKeywords = new[]
        {
            //"DROP", "DELETE", "ALTER", "CREATE", // ARTIK İZİN VERİLDİ
            "EXEC", "EXECUTE", "xp_", "sp_", "UNION"
            // TRUNCATE, UPDATE, INSERT artık özel whitelist ile kontrol ediliyor
        };

        // ===========================================================
        // 3) CİHAZ SİSTEM BİLGİSİ (Hostname + Seri No)
        // ===========================================================
        [HttpGet("devices/{deviceId}/system-info")]
        public async Task<IActionResult> GetDeviceSystemInfo(string deviceId)
        {
            var device = await _db.StoreDevices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null) return NotFound();

            string? hostname = null;
            string? serialNumber = null;
            string? hostnameError = null;
            string? serialError = null;

            try
            {
                var ht = await _remoteSqlService.ExecuteQueryAsync(
                    device.DbConnectionString,
                    "SELECT CAST(SERVERPROPERTY('MachineName') AS NVARCHAR(256)) AS Value");
                if (ht?.Rows.Count > 0)
                    hostname = ht.Rows[0]["Value"]?.ToString();
            }
            catch (Exception ex) { hostnameError = ex.Message; }

            try
            {
                var st = await _remoteSqlService.ExecuteQueryAsync(
                    device.DbConnectionString,
                    "EXEC xp_cmdshell 'wmic bios get SerialNumber /value'");
                if (st != null)
                {
                    foreach (DataRow row in st.Rows)
                    {
                        var line = row["output"]?.ToString()?.Trim() ?? "";
                        if (line.StartsWith("SerialNumber=", StringComparison.OrdinalIgnoreCase))
                        {
                            serialNumber = line.Split('=', 2)[1].Trim();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) { serialError = ex.Message; }

            return Ok(new { hostname, serialNumber, hostnameError, serialError });
        }

        // ===========================================================
        // 4) CİHAZ SİL (Envanter'den kaldır)
        // ===========================================================
        [HttpDelete("devices/{deviceId}")]
        public async Task<IActionResult> DeleteDevice(string deviceId)
        {
            var device = await _db.StoreDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null)
                return NotFound(new { error = "Device not found" });

            _db.StoreDevices.Remove(device);
            await _db.SaveChangesAsync();

            _logger.LogInformation("StoreDevice deleted: {DeviceId}", deviceId);
            DeviceStatusWorker.RemoveFromCache(deviceId); // Cache'den kaldır (null yapmadan)
            return Ok(new { success = true, deletedDeviceId = deviceId });
        }

        // ===========================================================
        // 5) GEÇİCİ KAPALI DURUMUNU GÜNCELLE
        // ===========================================================
        [HttpPut("devices/{deviceId}/temporary-close")]
        public async Task<IActionResult> ToggleTemporaryClose(string deviceId, [FromBody] TemporaryCloseRequest request)
        {
            var device = await _db.StoreDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null)
                return NotFound(new { error = "Device not found" });

            device.IsTemporarilyClosed = request.IsClosed;
            device.TemporaryCloseReason = request.IsClosed ? request.Reason : null;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Device {DeviceId} temporary close: {IsClosed}, reason: {Reason}",
                deviceId, request.IsClosed, request.Reason);

            // Cache'i null yapmadan sadece ilgili cihazı güncelle (dashboard yanıp sönmesin)
            DeviceStatusWorker.UpdateCachedDevice(deviceId, d =>
            {
                d.IsTemporarilyClosed = device.IsTemporarilyClosed;
                d.TemporaryCloseReason = device.TemporaryCloseReason;
            });

            return Ok(new { success = true, deviceId, isTemporarilyClosed = device.IsTemporarilyClosed });
        }

        [HttpPost("execute")]
        public async Task<IActionResult> ExecuteQuery([FromBody] ExecuteSqlQueryRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { error = "ModelState invalid" });

            if (string.IsNullOrWhiteSpace(request.Query))
                return BadRequest(new { error = "Query cannot be empty" });

            // 🔒 SQL Injection Prevention - Tehlikeli anahtar kelimeleri kontrol et
            var queryUpper = request.Query.ToUpperInvariant().Trim();
            
            // Önce özel izinli komutları kontrol et
            bool isSpecialCommand = AllowedSpecialCommands.Any(cmd => 
                queryUpper.Equals(cmd, StringComparison.OrdinalIgnoreCase));
            
            if (!isSpecialCommand)
            {
                // Blacklist control (only EXEC and SPs blocked for safety, but allowed most queries)
                foreach (var keyword in DangerousKeywords)
                {
                    if (queryUpper.Contains(keyword))
                    {
                        _logger.LogWarning("SQL Injection attempt detected - dangerous keyword: {Keyword}, Query: {Query}", 
                            keyword, request.Query);
                        return BadRequest(new { error = $"Query contains forbidden keyword: {keyword}" });
                    }
                }

                // Removed prefix restriction entirely per user request, allowing all non-blacklisted commands
            }
            else
            {
                _logger.LogInformation("Executing approved special command: {Query}", request.Query);
            }

            var device = await _db.StoreDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.DeviceId == request.DeviceId);

            if (device == null)
                return NotFound(new { error = "Device not found" });

            try
            {
                var json = await _remoteSqlService.ExecuteQueryAndReturnJsonAsync(
                    device.DbConnectionString,
                    request.Query
                );

                var result = System.Text.Json.JsonSerializer.Deserialize<object>(json);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQL error for device {DeviceId}", request.DeviceId);
                // Log the full error server-side but return a generic message to avoid leaking DB details
                return BadRequest(new { error = "Query execution failed. Check server logs for details." });
            }
        }
    }

    public class TemporaryCloseRequest
    {
        public bool IsClosed { get; set; }
        public string? Reason { get; set; }
    }
}
