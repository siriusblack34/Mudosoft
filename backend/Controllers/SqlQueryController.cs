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

        // In-memory cache for devices-with-status
        private static List<StoreDeviceWithStatusDto>? _cachedDevices;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly SemaphoreSlim _cacheLock = new(1, 1);
        private const int CacheSeconds = 15;

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
        // ===========================================================
        [HttpGet("devices/with-status")]
        public async Task<IActionResult> GetDevicesWithStatus(
            [FromQuery] int timeoutMs = 500,
            [FromQuery] int maxConcurrency = 40)
        {
            // Cache'den dön (15 saniye geçerli)
            if (_cachedDevices != null && DateTime.UtcNow < _cacheExpiry)
            {
                return Ok(_cachedDevices);
            }

            await _cacheLock.WaitAsync();
            try
            {
                // Double-check after acquiring lock
                if (_cachedDevices != null && DateTime.UtcNow < _cacheExpiry)
                {
                    return Ok(_cachedDevices);
                }

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
                        IsOnline = false,
                        LastSeen = d.LastSeen,
                        IsTemporarilyClosed = d.IsTemporarilyClosed,
                        TemporaryCloseReason = d.TemporaryCloseReason
                    })
                    .ToListAsync();

                using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));

                var tasks = devices.Select(async d =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        // Geçici cihazlar SQL Server olmayabilir — ping ile kontrol et
                        var isGecici = d.DeviceType.Equals("gecici", StringComparison.OrdinalIgnoreCase)
                                    || d.DeviceType.Equals("GEÇİCİ", StringComparison.OrdinalIgnoreCase);
                        if (isGecici)
                        {
                            try
                            {
                                using var ping = new System.Net.NetworkInformation.Ping();
                                var reply = await ping.SendPingAsync(d.CalculatedIpAddress, timeoutMs);
                                d.IsOnline = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
                            }
                            catch { d.IsOnline = false; }
                        }
                        else
                        {
                            d.IsOnline = await _fastCheck.IsSqlReachableAsync(
                                d.CalculatedIpAddress,
                                1433,
                                timeoutMs
                            );
                        }
                    }
                    finally
                    {
                        sem.Release();
                    }
                });

                await Task.WhenAll(tasks);

                // Online olan cihazların LastSeen'ini güncelle
                var onlineIds = devices.Where(d => d.IsOnline).Select(d => d.DeviceId).ToHashSet();
                if (onlineIds.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    var toUpdate = await _db.StoreDevices
                        .Where(d => onlineIds.Contains(d.DeviceId))
                        .ToListAsync();
                    foreach (var d in toUpdate)
                        d.LastSeen = now;
                    await _db.SaveChangesAsync();

                    foreach (var d in devices.Where(d => d.IsOnline))
                        d.LastSeen = now;
                }

                // ─── Offline Store Logging ───
                try
                {
                    await UpdateOfflineLogsAsync(devices);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Offline log update failed (non-critical)");
                }

                // Cache'i güncelle
                _cachedDevices = devices;
                _cacheExpiry = DateTime.UtcNow.AddSeconds(CacheSeconds);

                return Ok(devices);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        // ===========================================================
        // 1.5) OFFLİNE LOG GÜNCELLEME
        // ===========================================================
        private async Task UpdateOfflineLogsAsync(List<StoreDeviceWithStatusDto> devices)
        {
            var now = DateTime.UtcNow;

            // Mağaza bazında kasa durumlarını grupla (PC hariç, geçici kapalı hariç)
            var storeStatus = devices
                .Where(d => d.DeviceType?.ToUpper() != "PC" && !d.IsTemporarilyClosed)
                .GroupBy(d => d.StoreCode)
                .Select(g => new
                {
                    StoreCode = g.Key,
                    StoreName = g.First().StoreName,
                    AllOffline = g.All(k => !k.IsOnline),
                    OfflineCount = g.Count(k => !k.IsOnline)
                })
                .ToList();

            // Açık (kapanmamış) log kayıtlarını al
            var openLogs = await _db.StoreOfflineLogs
                .Where(l => l.OnlineAt == null)
                .ToListAsync();

            var openLogMap = openLogs.ToDictionary(l => l.StoreCode);

            foreach (var store in storeStatus)
            {
                if (store.AllOffline)
                {
                    // Tüm kasalar offline - açık log yoksa oluştur
                    if (!openLogMap.ContainsKey(store.StoreCode))
                    {
                        _db.StoreOfflineLogs.Add(new StoreOfflineLog
                        {
                            StoreCode = store.StoreCode,
                            StoreName = store.StoreName,
                            OfflineKasaCount = store.OfflineCount,
                            OfflineAt = now
                        });
                    }
                }
                else
                {
                    // En az bir kasa online - açık log varsa kapat
                    if (openLogMap.TryGetValue(store.StoreCode, out var log))
                    {
                        log.OnlineAt = now;
                        log.DurationMinutes = (int)(now - log.OfflineAt).TotalMinutes;
                    }
                }
            }

            await _db.SaveChangesAsync();
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
            _cachedDevices = null; // Cache invalidate
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

            _cachedDevices = null; // Cache invalidate

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
