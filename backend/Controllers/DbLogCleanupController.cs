using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Middleware;
using Orchestra.Backend.Services;
using System.Data;
using System.Text.Json;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [RequireMenu("/cleanup")] // Temizlik Merkezi menüsüne bağlı
    [Route("api/db-log-cleanup")]
    public class DbLogCleanupController : ControllerBase
    {
        private readonly OrchestraDbContext _db;
        private readonly IRemoteSqlService _remoteSqlService;
        private readonly ILogger<DbLogCleanupController> _logger;
        private readonly FastSqlReachabilityService _fastCheck;
        private readonly ActivityLogService _activity;

        public DbLogCleanupController(
            OrchestraDbContext db,
            IRemoteSqlService remoteSqlService,
            FastSqlReachabilityService fastCheck,
            ILogger<DbLogCleanupController> logger,
            ActivityLogService activity)
        {
            _db = db;
            _remoteSqlService = remoteSqlService;
            _fastCheck = fastCheck;
            _logger = logger;
            _activity = activity;
        }

        public class DbLogStatusDto
        {
            public string DeviceId { get; set; } = "";
            public int StoreCode { get; set; }
            public string StoreName { get; set; } = "";
            public string IpAddress { get; set; } = "";
            public bool IsOnline { get; set; }
            public int ExportLogCount { get; set; }
            public int ExportErrLogCount { get; set; }
            public int Total { get; set; }
            public string Status { get; set; } = "unknown";
            public string? ErrorMessage { get; set; }
        }

        // ===========================================================
        // 1) TÜM PC'LERİ KONTROL ET
        // ===========================================================
        [HttpPost("check-all")]
        public async Task<IActionResult> CheckAll()
        {
            var pcDevices = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici")
                .ToListAsync();

            var results = new List<DbLogStatusDto>();
            using var sem = new SemaphoreSlim(20); 

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync();
                try
                {
                    var dto = new DbLogStatusDto
                    {
                        DeviceId = device.DeviceId,
                        StoreCode = device.StoreCode,
                        StoreName = device.StoreName,
                        IpAddress = device.CalculatedIpAddress
                    };

                    // Online kontrolü (1433 portu)
                    var isOnline = await _fastCheck.IsSqlReachableAsync(device.CalculatedIpAddress, 1433, 2000);
                    dto.IsOnline = isOnline;

                    if (!isOnline)
                    {
                        dto.Status = "offline";
                        lock (results) results.Add(dto);
                        return;
                    }

                    // SQL Sorgusu - Table countları al
                    string query = @"
                        SELECT 
                            (SELECT COUNT(*) FROM EXPORT_LOG) as VExportLogCount,
                            (SELECT COUNT(*) FROM EXPORT_ERR_LOG) as ExportErrLogCount
                    ";

                    var dt = await _remoteSqlService.ExecuteQueryAsync(device.DbConnectionString, query);

                    if (dt != null && dt.Rows.Count > 0)
                    {
                        var row = dt.Rows[0];
                        dto.ExportLogCount = row["VExportLogCount"] != DBNull.Value ? Convert.ToInt32(row["VExportLogCount"]) : 0;
                        dto.ExportErrLogCount = row["ExportErrLogCount"] != DBNull.Value ? Convert.ToInt32(row["ExportErrLogCount"]) : 0;
                        dto.Total = dto.ExportLogCount + dto.ExportErrLogCount;
                        
                        dto.Status = dto.Total > 0 ? "dirty" : "clean";
                    }
                    else
                    {
                        // Tablo boş veya bulunamadı
                        dto.Status = "clean";
                    }
                    
                    lock (results) results.Add(dto);
                }
                catch (Exception ex)
                {
                    lock (results)
                    {
                        var dto = new DbLogStatusDto
                        {
                            DeviceId = device.DeviceId,
                            StoreCode = device.StoreCode,
                            StoreName = device.StoreName,
                            IpAddress = device.CalculatedIpAddress,
                            IsOnline = true,
                            Status = "error",
                            ErrorMessage = ex.Message
                        };
                        results.Add(dto);

                        _logger.LogError(ex, "DbLogCleanup error — Device: {StoreName} ({IpAddress})",
                            device.StoreName, device.CalculatedIpAddress);
                    }
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);

            var ordered = results.OrderBy(r => r.StoreCode).ToList();
            return Ok(ordered);
        }

        // ===========================================================
        // 2) TEK PC TEMİZLE
        // ===========================================================
        [HttpPost("clean/{deviceId}")]
        public async Task<IActionResult> CleanSingle(string deviceId)
        {
            var device = await _db.StoreDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId);

            if (device == null)
                return NotFound(new { error = "Device not found" });

            try
            {
                // EXPORT_LOG ve EXPORT_ERR_LOG tablolarını temizle
                string query = "TRUNCATE TABLE EXPORT_LOG\nGO\nTRUNCATE TABLE EXPORT_ERR_LOG";
                await _remoteSqlService.ExecuteQueryAsync(device.DbConnectionString, query);

                _logger.LogInformation("EXPORT_LOG and EXPORT_ERR_LOG truncated: {DeviceId} ({StoreName})", device.DeviceId, device.StoreName);
                await _activity.LogAsync("Cleanup", "DbLogCleanSingle", $"{device.StoreName} ({device.DeviceId})", "EXPORT_LOG + EXPORT_ERR_LOG truncate");
                return Ok(new { success = true, message = $"{device.StoreName} log tabloları temizlendi." });
            }
            catch (Exception ex)
            {
                await _activity.LogAsync("Cleanup", "DbLogCleanSingle", $"{device.StoreName} ({device.DeviceId})", null, false, ex.Message);
                return BadRequest(new { error = ex.Message });
            }
        }

        // ===========================================================
        // 3) TÜM PC'LERİ TEMİZLE
        // ===========================================================
        [HttpPost("clean-all")]
        public async Task<IActionResult> CleanAll()
        {
            var pcDevices = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici")
                .ToListAsync();

            var results = new List<object>();
            using var sem = new SemaphoreSlim(20); 

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync();
                try
                {
                    // Online kontrolü (Hızlı Check)
                    var isOnline = await _fastCheck.IsSqlReachableAsync(device.CalculatedIpAddress, 1433, 1000);
                    
                    if (isOnline)
                    {
                        string query = "TRUNCATE TABLE EXPORT_LOG\nGO\nTRUNCATE TABLE EXPORT_ERR_LOG";
                        await _remoteSqlService.ExecuteQueryAsync(device.DbConnectionString, query);
                        
                        lock (results) results.Add(new { deviceId = device.DeviceId, status = "cleaned", message = "Temizlendi" });
                    }
                    else
                    {
                        lock (results) results.Add(new { deviceId = device.DeviceId, status = "offline", message = "Cihaz offline" });
                    }
                }
                catch (Exception ex)
                {
                    lock (results) results.Add(new { deviceId = device.DeviceId, status = "error", message = ex.Message });
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);

            var cleanedCount = results.Count(r => ((dynamic)r).status == "cleaned");
            await _activity.LogAsync("Cleanup", "DbLogCleanAll", null, $"{cleanedCount}/{pcDevices.Count} PC temizlendi");

            return Ok(new { success = true, total = pcDevices.Count, details = results });
        }
    }
}
