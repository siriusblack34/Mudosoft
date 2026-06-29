using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orchestra.Backend.Data;
using Orchestra.Backend.Middleware;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize] // 🔒 Authentication required
    [Route("api/sqlquery")]
    public class SqlQueryController : ControllerBase
    {
        private readonly IRemoteSqlService _remoteSqlService;
        private readonly OrchestraDbContext _db;
        private readonly ILogger<SqlQueryController> _logger;
        private readonly FastSqlReachabilityService _fastCheck;

        // Cache artık DeviceStatusWorker tarafından yönetiliyor

        public SqlQueryController(
            IRemoteSqlService remoteSqlService,
            OrchestraDbContext db,
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
                    TemporaryCloseReason = d.TemporaryCloseReason,
                    WindowsVersion = d.WindowsVersion
                })
                .ToListAsync();

            return Ok(devices);
        }

        // ===========================================================
        // 1.5) WINDOWS SÜRÜM TARAMA
        // ===========================================================

        /// <summary>
        /// Tüm PC/Kasa'ların Windows sürümünü SMB üzerinden tarar ve DB'ye kaydeder.
        /// kernel32.dll ProductVersion → build numarası → "Win10 22H2" gibi okunabilir forma dönüştürülür.
        /// Agent gerekmez — C$ paylaşımı yeterli.
        /// POST: /api/sqlquery/refresh-windows-versions
        /// </summary>
        [HttpPost("refresh-windows-versions")]
        public async Task<IActionResult> RefreshWindowsVersions()
        {
            var pcDevices = await _db.StoreDevices
                .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici" ||
                            d.DeviceType.StartsWith("Kasa"))
                .ToListAsync();

            var updated = 0;
            var failed = 0;
            using var sem = new SemaphoreSlim(20);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync(cts.Token);
                try
                {
                    var version = await GetWindowsVersionAsync(device.CalculatedIpAddress);
                    if (version != null && version != device.WindowsVersion)
                    {
                        device.WindowsVersion = version;
                        Interlocked.Increment(ref updated);
                    }
                    else if (version == null)
                        Interlocked.Increment(ref failed);
                }
                catch { Interlocked.Increment(ref failed); }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);
            await _db.SaveChangesAsync();

            // Cache'i de güncelle
            DeviceStatusWorker.InvalidateCache();

            _logger.LogInformation("Windows version refresh: {Updated} güncellendi, {Failed} erişilemedi", updated, failed);
            return Ok(new { scanned = pcDevices.Count, updated, failed });
        }

        private static async Task<string?> GetWindowsVersionAsync(string ip)
        {
            var kernel32 = $@"\\{ip}\C$\Windows\System32\kernel32.dll";
            try
            {
                var task = Task.Run(() => FileVersionInfo.GetVersionInfo(kernel32));
                if (await Task.WhenAny(task, Task.Delay(5000)) != task || !task.IsCompletedSuccessfully)
                    return null;
                var vi = task.Result;
                return MapBuildToVersion(vi.ProductBuildPart);
            }
            catch { return null; }
        }

        private static string MapBuildToVersion(int build) => build switch
        {
            >= 26100 => "Win11 24H2",
            >= 22631 => "Win11 23H2",
            >= 22621 => "Win11 22H2",
            >= 22000 => "Win11 21H2",
            20348    => "Server 2022",
            >= 19045 => "Win10 22H2",
            >= 19044 => "Win10 21H2",
            >= 19043 => "Win10 21H1",
            >= 19042 => "Win10 20H2",
            >= 19041 => "Win10 2004",
            >= 18363 => "Win10 1909",
            >= 17763 => "Win10 1809",
            >= 14393 => "Win10 1607 / Svr2016",
            >= 9600  => "Win 8.1",
            >= 7600  => "Win 7",
            _        => $"Build {build}"
        };

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

            // Oncelik sirasi (her alan icin manuel override en yuksek):
            //   hostname:     StoreDevices.Hostname > Devices.Hostname > SQL SERVERPROPERTY
            //   serialNumber: StoreDevices.SerialNumber > Devices.SerialNumber
            string? hostname = string.IsNullOrWhiteSpace(device.Hostname) ? null : device.Hostname;
            string? serialNumber = string.IsNullOrWhiteSpace(device.SerialNumber) ? null : device.SerialNumber;
            string? hostnameError = null;
            string? serialError = null;

            if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(serialNumber))
            {
                var managed = await _db.Devices.AsNoTracking()
                    .Where(d => d.IpAddress == device.CalculatedIpAddress)
                    .Select(d => new { d.Hostname, d.SerialNumber })
                    .FirstOrDefaultAsync();
                if (managed != null)
                {
                    if (string.IsNullOrWhiteSpace(hostname))
                        hostname = string.IsNullOrWhiteSpace(managed.Hostname) ? null : managed.Hostname;
                    if (string.IsNullOrWhiteSpace(serialNumber))
                        serialNumber = managed.SerialNumber;
                }
            }

            if (string.IsNullOrWhiteSpace(hostname))
            {
                try
                {
                    var ht = await _remoteSqlService.ExecuteQueryAsync(
                        device.DbConnectionString,
                        "SELECT CAST(SERVERPROPERTY('MachineName') AS NVARCHAR(256)) AS Value");
                    if (ht?.Rows.Count > 0)
                        hostname = ht.Rows[0]["Value"]?.ToString();
                }
                catch (Exception ex) { hostnameError = ex.Message; }
            }

            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                serialError = "Seri henuz cekilmedi — bir sonraki aylik sync'te doldurulacak.";
            }

            var printerSerialNumber = string.IsNullOrWhiteSpace(device.PrinterSerialNumber) ? null : device.PrinterSerialNumber;

            return Ok(new { hostname, serialNumber, hostnameError, serialError, printerSerialNumber });
        }

        // ===========================================================
        // 3b) KASA MANUEL BILGI GUNCELLEME (hostname / BIOS seri / yazici sicil)
        //     Body'de gonderilmeyen (undefined) alanlar dokunulmaz; null gonderilen temizlenir.
        // ===========================================================
        public class UpdateManualInfoRequest
        {
            public string? Hostname { get; set; }
            public string? SerialNumber { get; set; }
            public string? PrinterSerialNumber { get; set; }
            // Hangi alanlarin gonderildigini ayirt etmek icin
            public bool HasHostname { get; set; }
            public bool HasSerialNumber { get; set; }
            public bool HasPrinterSerialNumber { get; set; }
        }

        [HttpPut("devices/{deviceId}/manual-info")]
        public async Task<IActionResult> UpdateManualInfo(string deviceId, [FromBody] System.Text.Json.JsonElement body)
        {
            var device = await _db.StoreDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null) return NotFound(new { error = "Device not found" });

            string? Norm(string? v) { var t = v?.Trim(); return string.IsNullOrWhiteSpace(t) ? null : t; }

            if (body.TryGetProperty("hostname", out var hp))
                device.Hostname = hp.ValueKind == System.Text.Json.JsonValueKind.Null ? null : Norm(hp.GetString());
            if (body.TryGetProperty("serialNumber", out var sp))
                device.SerialNumber = sp.ValueKind == System.Text.Json.JsonValueKind.Null ? null : Norm(sp.GetString());
            if (body.TryGetProperty("printerSerialNumber", out var pp))
                device.PrinterSerialNumber = pp.ValueKind == System.Text.Json.JsonValueKind.Null ? null : Norm(pp.GetString());

            await _db.SaveChangesAsync();
            _logger.LogInformation("Kasa manuel bilgi guncellendi: {DeviceId} host={Host} serial={Serial} printer={Printer} by {User}",
                deviceId, device.Hostname, device.SerialNumber, device.PrinterSerialNumber, User?.Identity?.Name);

            return Ok(new
            {
                deviceId,
                hostname = device.Hostname,
                serialNumber = device.SerialNumber,
                printerSerialNumber = device.PrinterSerialNumber
            });
        }

        // Geriye uyumluluk: eski endpoint duruyor
        public class UpdatePrinterSerialRequest
        {
            public string? PrinterSerialNumber { get; set; }
        }

        [HttpPut("devices/{deviceId}/printer-serial")]
        public async Task<IActionResult> UpdatePrinterSerial(string deviceId, [FromBody] UpdatePrinterSerialRequest req)
        {
            var device = await _db.StoreDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null) return NotFound(new { error = "Device not found" });

            var trimmed = req.PrinterSerialNumber?.Trim();
            device.PrinterSerialNumber = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Printer sicil manuel guncellendi: {DeviceId} -> {Value} by {User}",
                deviceId, device.PrinterSerialNumber ?? "(temizlendi)", User?.Identity?.Name);

            return Ok(new { deviceId, printerSerialNumber = device.PrinterSerialNumber });
        }

        // ===========================================================
        // 3d) TEK KASA ICIN PRINTER SICIL'I YENIDEN CEK (Geçici SQL kesintisi sonrasi retry icin)
        // ===========================================================
        [HttpPost("devices/{deviceId}/refresh-printer-serial")]
        public async Task<IActionResult> RefreshPrinterSerial(string deviceId, CancellationToken ct)
        {
            var device = await _db.StoreDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null) return NotFound(new { error = "Device not found" });
            if (string.IsNullOrWhiteSpace(device.DbConnectionString))
                return BadRequest(new { error = "Cihazin DB connection string'i yok" });

            try
            {
                var dt = await _remoteSqlService.ExecuteQueryAsync(
                    device.DbConnectionString,
                    "SELECT TOP 1 PARAMETER_1 AS sicil FROM TRANSACTION_RESULT WHERE PARAMETER_1 LIKE 'YAB%' ORDER BY CREATE_DATE DESC");

                var raw = dt?.Rows.Count > 0 ? dt.Rows[0]["sicil"]?.ToString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(raw))
                    return Ok(new { deviceId, printerSerialNumber = device.PrinterSerialNumber, message = "Kasanin DB'sinde YAB ile baslayan kayit bulunamadi" });

                device.PrinterSerialNumber = raw;
                await _db.SaveChangesAsync();
                _logger.LogInformation("Printer sicil tek-kasa refresh: {DeviceId} -> {Serial}", deviceId, raw);
                return Ok(new { deviceId, printerSerialNumber = raw });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ===========================================================
        // 3c) PRINTER SICIL TUM KASALAR ICIN SYNC (Admin manuel tetik)
        // ===========================================================
        [HttpPost("devices/sync-printer-serials")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SyncPrinterSerials(
            [FromServices] PrinterSerialSyncService syncService,
            CancellationToken ct)
        {
            var result = await syncService.RunSyncAsync(ct);
            return Ok(new
            {
                skipped = result.Skipped,
                total = result.Total,
                updated = result.Updated,
                unchanged = result.Unchanged,
                failed = result.Failed,
                completedAtUtc = result.CompletedAtUtc?.ToString("o")
            });
        }

        // ===========================================================
        // 3e) GEÇİCİ PC DURUMU — hangi mağaza kurulu, aktif mi boş mu
        // ===========================================================
        [HttpGet("gecici/status")]
        public async Task<IActionResult> GetGeciciStatus(CancellationToken ct)
        {
            var geciciDevices = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "GECICI")
                .ToListAsync(ct);

            var cached = DeviceStatusWorker.GetCachedDevices();

            // Tüm mağaza isimlerini tek sorguda önceden al
            var storeNameList = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC" || d.DeviceType.StartsWith("Kasa"))
                .GroupBy(d => d.StoreCode)
                .Select(g => new { Code = g.Key, Name = g.First().StoreName })
                .ToListAsync(ct);
            var storeNameMap = storeNameList.ToDictionary(x => x.Code, x => x.Name);

            using var sem = new SemaphoreSlim(3);

            var tasks = geciciDevices.Select(async device =>
            {
                var status = cached?.FirstOrDefault(c => c.DeviceId == device.DeviceId);
                var pingOk = status?.PingReachable == true;
                var sqlOk = status?.SqlReachable == true;

                int? installedStoreCode = null;
                string? installedStoreName = null;

                if (sqlOk && !string.IsNullOrEmpty(device.DbConnectionString))
                {
                    await sem.WaitAsync(ct);
                    try
                    {
                        // Connect Timeout kısalt — cihaz zaten SQL reachable olarak işaretlendi
                        var connStr = System.Text.RegularExpressions.Regex.Replace(
                            device.DbConnectionString,
                            @"Connect Timeout=\d+",
                            "Connect Timeout=5");

                        var dt = await _remoteSqlService.ExecuteQueryAsync(
                            connStr, "SELECT PARAMETER_VALUE FROM PSS_HOME WHERE ID=69");

                        if (dt?.Rows.Count > 0)
                        {
                            var raw = dt.Rows[0]["PARAMETER_VALUE"]?.ToString();
                            if (int.TryParse(raw, out var rawCode) && rawCode > 1000)
                            {
                                installedStoreCode = rawCode - 1000;
                                storeNameMap.TryGetValue(installedStoreCode.Value, out installedStoreName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Gecici PC {DeviceId} PSS_HOME sorgusu basarisiz: {Error}",
                            device.DeviceId, ex.Message);
                    }
                    finally { sem.Release(); }
                }

                return new GeciciPcStatusDto
                {
                    DeviceId = device.DeviceId,
                    DeviceName = device.DeviceName ?? device.DeviceId,
                    IpAddress = device.CalculatedIpAddress ?? "",
                    PingReachable = pingOk,
                    SqlReachable = sqlOk,
                    IsActive = sqlOk,
                    InstalledStoreCode = installedStoreCode,
                    InstalledStoreName = installedStoreName,
                };
            });

            var results = await Task.WhenAll(tasks);
            return Ok(results.OrderBy(r => r.DeviceName));
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
        [RequireMenu("/sql-query")] // ham SQL çalıştırma yalnızca SQL Sorgu menüsü olanlara
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

    public class GeciciPcStatusDto
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public bool PingReachable { get; set; }
        public bool SqlReachable { get; set; }
        public bool IsActive { get; set; }
        public int? InstalledStoreCode { get; set; }
        public string? InstalledStoreName { get; set; }
    }
}
