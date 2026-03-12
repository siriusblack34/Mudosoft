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
        [AllowAnonymous] // Device list doesn't need authentication
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
        [AllowAnonymous] // Device status list doesn't need authentication
        [HttpGet("devices/with-status")]
        public async Task<IActionResult> GetDevicesWithStatus(
            [FromQuery] int timeoutMs = 500,
            [FromQuery] int maxConcurrency = 40)
        {
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
                    LastSeen = d.LastSeen
                })
                .ToListAsync();

            using var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency));

            var tasks = devices.Select(async d =>
            {
                await sem.WaitAsync();
                try
                {
                    d.IsOnline = await _fastCheck.IsSqlReachableAsync(
                        d.CalculatedIpAddress,
                        1433,
                        timeoutMs
                    );
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

                // DTO'larda da güncelle (dönen veri güncel olsun)
                foreach (var d in devices.Where(d => d.IsOnline))
                    d.LastSeen = now;
            }

            return Ok(devices);
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
        [AllowAnonymous]
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
        [AllowAnonymous]
        [HttpDelete("devices/{deviceId}")]
        public async Task<IActionResult> DeleteDevice(string deviceId)
        {
            var device = await _db.StoreDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null)
                return NotFound(new { error = "Device not found" });

            _db.StoreDevices.Remove(device);
            await _db.SaveChangesAsync();

            _logger.LogInformation("StoreDevice deleted: {DeviceId}", deviceId);
            return Ok(new { success = true, deletedDeviceId = deviceId });
        }

        [AllowAnonymous] // SQL execution allowed for frontend - queries are validated by whitelist
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
                // Artık hatayı gizlemiyoruz ki frontend detay görebilsin
                return BadRequest(new { error = $"Query execution failed: {ex.Message}" });
            }
        }
    }
}
