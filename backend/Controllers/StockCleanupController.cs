using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Services;
using System.Data;
using System.Text.Json;

namespace MudoSoft.Backend.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/stock-cleanup")]
    public class StockCleanupController : ControllerBase
    {
        private readonly MudoSoftDbContext _db;
        private readonly IRemoteSqlService _remoteSqlService;
        private readonly ILogger<StockCleanupController> _logger;
        private readonly FastSqlReachabilityService _fastCheck;

        public StockCleanupController(
            MudoSoftDbContext db,
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
        // 3) TÜM PC'LERİ TEMİZLE (TRUNCATE ALL)
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
                        string query = "TRUNCATE TABLE POS_STOCK_TRANSFER";
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

            return Ok(new { success = true, total = pcDevices.Count, details = results });
        }
    }
}
