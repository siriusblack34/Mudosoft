using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;
using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/stock-cleanup")]
    public class StockCleanupController : ControllerBase
    {
        private readonly OrchestraDbContext _db;
        private readonly IRemoteSqlService _remoteSqlService;
        private readonly ILogger<StockCleanupController> _logger;
        private readonly FastSqlReachabilityService _fastCheck;

        private static readonly ConcurrentDictionary<string, CleanAllJob> _cleanAllJobs = new();

        public StockCleanupController(
            OrchestraDbContext db,
            IRemoteSqlService remoteSqlService,
            FastSqlReachabilityService fastCheck,
            ILogger<StockCleanupController> logger)
        {
            _db = db;
            _remoteSqlService = remoteSqlService;
            _fastCheck = fastCheck;
            _logger = logger;
        }

        public class StockStatusDto
        {
            public string DeviceId { get; set; } = "";
            public int StoreCode { get; set; }
            public string StoreName { get; set; } = "";
            public string IpAddress { get; set; } = "";
            public string DeviceType { get; set; } = "";
            public bool IsOnline { get; set; }
            public int Plu0 { get; set; }
            public int Plu10 { get; set; }
            public int Plu20 { get; set; }
            public int Plu30 { get; set; } // > 30
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

            var results = new List<StockStatusDto>();
            using var sem = new SemaphoreSlim(20); 

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync();
                try
                {
                    var dto = new StockStatusDto
                    {
                        DeviceId = device.DeviceId,
                        StoreCode = device.StoreCode,
                        StoreName = device.StoreName,
                        IpAddress = device.CalculatedIpAddress,
                        DeviceType = device.DeviceType
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

                    // SQL Sorgusu
                    /*
                     * OK durumlarına göre sayım yapıyoruz.
                     * OK=0  -> İşlenmemiş
                     * OK=10 -> İşleniyor
                     * OK=20 -> Başarılı
                     * OK>30 -> Hata
                     */
                    string query = @"
                        SELECT 
                            COALESCE(SUM(CASE WHEN OK = 0 THEN 1 ELSE 0 END), 0) as Plu0,
                            COALESCE(SUM(CASE WHEN OK = 10 THEN 1 ELSE 0 END), 0) as Plu10,
                            COALESCE(SUM(CASE WHEN OK = 20 THEN 1 ELSE 0 END), 0) as Plu20,
                            COALESCE(SUM(CASE WHEN OK > 30 THEN 1 ELSE 0 END), 0) as Plu30,
                            COUNT(*) as Total
                        FROM POS_STOCK_TRANSFER";

                    var dt = await _remoteSqlService.ExecuteQueryAsync(device.DbConnectionString, query);

                    if (dt != null && dt.Rows.Count > 0)
                    {
                        var row = dt.Rows[0];
                        dto.Plu0 = row["Plu0"] != DBNull.Value ? Convert.ToInt32(row["Plu0"]) : 0;
                        dto.Plu10 = row["Plu10"] != DBNull.Value ? Convert.ToInt32(row["Plu10"]) : 0;
                        dto.Plu20 = row["Plu20"] != DBNull.Value ? Convert.ToInt32(row["Plu20"]) : 0;
                        dto.Plu30 = row["Plu30"] != DBNull.Value ? Convert.ToInt32(row["Plu30"]) : 0;
                        dto.Total = row["Total"] != DBNull.Value ? Convert.ToInt32(row["Total"]) : 0;
                        
                        // Eğer >30 (Hata) varsa veya hiç kayıt yoksa 'clean' diyemeyiz gibi bir mantık olabilir
                        // Ancak burada sadece temizlenecek kayıt var mı ona bakıyoruz.
                        // Kullanıcı talebi: "sorunlu kayıtların (OK != 0, 10, 20) adetlerinin..."
                        // Aslında biz hepsini listeliyoruz şu an.
                        
                        // Status mantığı: Eğer toplam > 0 ise tablo doludur, "dirty" diyelim.
                        // Ya da sadece >30 varsa mı dirty?
                        // Şimdilik Total > 0 ise dirty diyelim, kullanıcı hepsini silmek isteyebilir.
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
                        var dto = new StockStatusDto
                        {
                            DeviceId = device.DeviceId,
                            StoreCode = device.StoreCode,
                            StoreName = device.StoreName,
                            IpAddress = device.CalculatedIpAddress,
                            DeviceType = device.DeviceType,
                            IsOnline = true,
                            Status = "error",
                            ErrorMessage = ex.Message
                        };
                        results.Add(dto);

                        // DEBUG LOG
                        try {
                            System.IO.File.AppendAllText("C:\\Projects\\mudosoft\\backend\\debug_error.txt", 
                                $"[{DateTime.Now}] Device: {device.StoreName} ({device.CalculatedIpAddress})\nError: {ex.Message}\nStack: {ex.StackTrace}\n--------------------------------------------------\n");
                        } catch { /* ignore file log errors */ }
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
        // 2) TEK PC TEMİZLE (TRUNCATE)
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
                string query = "TRUNCATE TABLE POS_STOCK_TRANSFER";
                await _remoteSqlService.ExecuteQueryAsync(device.DbConnectionString, query);

                _logger.LogInformation("POS_STOCK_TRANSFER truncated: {DeviceId} ({StoreName})", device.DeviceId, device.StoreName);
                return Ok(new { success = true, message = $"{device.StoreName} tablosu temizlendi." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // ===========================================================
        // 3) TÜM PC'LERİ TEMİZLE (TRUNCATE ALL) — JOB-BASED
        // ===========================================================
        [HttpPost("clean-all")]
        public async Task<IActionResult> CleanAll([FromServices] IServiceScopeFactory scopeFactory)
        {
            var pcCount = await _db.StoreDevices
                .AsNoTracking()
                .CountAsync(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici");

            var jobId = Guid.NewGuid().ToString("N")[..8];
            var job = new CleanAllJob
            {
                JobId = jobId,
                TotalCount = pcCount,
                StartedAtUtc = DateTime.UtcNow
            };
            _cleanAllJobs[jobId] = job;

            _ = Task.Run(() => ProcessCleanAllJobAsync(jobId, scopeFactory));

            _logger.LogInformation("Stock clean-all job queued: JobId={JobId} TargetCount={Count}", jobId, pcCount);
            return Accepted(new { jobId, totalCount = pcCount });
        }

        // ===========================================================
        // 4) TOPLU TEMİZLİK JOB DURUMU
        // ===========================================================
        [HttpGet("clean-all/{jobId}")]
        public IActionResult GetCleanAllStatus(string jobId)
        {
            if (!_cleanAllJobs.TryGetValue(jobId, out var job))
                return NotFound(new { error = "Job bulunamadi" });

            lock (job.Lock)
            {
                return Ok(new
                {
                    jobId = job.JobId,
                    totalCount = job.TotalCount,
                    completedCount = job.CompletedCount,
                    successCount = job.SuccessCount,
                    offlineCount = job.OfflineCount,
                    errorCount = job.ErrorCount,
                    lastDeviceName = job.LastDeviceName,
                    startedAtUtc = job.StartedAtUtc.ToString("o"),
                    completedAtUtc = job.CompletedAtUtc?.ToString("o"),
                    isCompleted = job.CompletedAtUtc.HasValue,
                    error = job.Error
                });
            }
        }

        private async Task ProcessCleanAllJobAsync(string jobId, IServiceScopeFactory scopeFactory)
        {
            if (!_cleanAllJobs.TryGetValue(jobId, out var job)) return;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
                var remoteSql = scope.ServiceProvider.GetRequiredService<IRemoteSqlService>();
                var fastCheck = scope.ServiceProvider.GetRequiredService<FastSqlReachabilityService>();

                var pcDevices = await db.StoreDevices
                    .AsNoTracking()
                    .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici")
                    .ToListAsync();

                using var sem = new SemaphoreSlim(20);

                var tasks = pcDevices.Select(async device =>
                {
                    await sem.WaitAsync();
                    string status;
                    try
                    {
                        var isOnline = await fastCheck.IsSqlReachableAsync(device.CalculatedIpAddress, 1433, 1000);
                        if (!isOnline)
                        {
                            status = "offline";
                        }
                        else
                        {
                            await remoteSql.ExecuteQueryAsync(device.DbConnectionString, "TRUNCATE TABLE POS_STOCK_TRANSFER");
                            status = "cleaned";
                        }
                    }
                    catch (Exception ex)
                    {
                        status = "error";
                        _logger.LogWarning(ex, "Stock clean-all device error: {DeviceId}", device.DeviceId);
                    }
                    finally
                    {
                        sem.Release();
                    }

                    lock (job.Lock)
                    {
                        job.CompletedCount++;
                        if (status == "cleaned") job.SuccessCount++;
                        else if (status == "offline") job.OfflineCount++;
                        else job.ErrorCount++;
                        job.LastDeviceName = device.StoreName;
                    }
                });

                await Task.WhenAll(tasks);

                lock (job.Lock) job.CompletedAtUtc = DateTime.UtcNow;
                _logger.LogInformation("Stock clean-all job {JobId} done: success={S} offline={O} error={E}",
                    jobId, job.SuccessCount, job.OfflineCount, job.ErrorCount);
            }
            catch (Exception ex)
            {
                lock (job.Lock)
                {
                    job.Error = ex.Message;
                    job.CompletedAtUtc = DateTime.UtcNow;
                }
                _logger.LogError(ex, "Stock clean-all job {JobId} failed", jobId);
            }
        }

        private sealed class CleanAllJob
        {
            public string JobId { get; init; } = "";
            public int TotalCount { get; set; }
            public int CompletedCount { get; set; }
            public int SuccessCount { get; set; }
            public int OfflineCount { get; set; }
            public int ErrorCount { get; set; }
            public string? LastDeviceName { get; set; }
            public DateTime StartedAtUtc { get; set; }
            public DateTime? CompletedAtUtc { get; set; }
            public string? Error { get; set; }
            public object Lock { get; } = new();
        }
    }
}
