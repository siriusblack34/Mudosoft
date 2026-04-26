using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Services;

namespace MudoSoft.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/diagnostics")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly NetworkDiagnosticsService _diagnostics;
        private readonly ILogger<DiagnosticsController> _logger;

        public DiagnosticsController(
            NetworkDiagnosticsService diagnostics,
            ILogger<DiagnosticsController> logger)
        {
            _diagnostics = diagnostics;
            _logger = logger;
        }

        /// <summary>
        /// Aktif ag sorunlarini analiz eder.
        /// SqlQueryController'daki son durum verisiyle calisir.
        /// </summary>
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveDiagnostics(
            [FromQuery] int windowMinutes = 30,
            [FromQuery] int flappingThreshold = 4,
            [FromServices] Data.MudoSoftDbContext db = default!,
            [FromServices] FastSqlReachabilityService fastCheck = default!)
        {
            // En son cihaz durumunu al (cache varsa cache'den)
            var devices = await GetCurrentDeviceStatusAsync(db, fastCheck);

            var results = await _diagnostics.AnalyzeAllStoresAsync(
                devices, windowMinutes, flappingThreshold);

            var summary = new
            {
                AnalyzedAt = DateTime.UtcNow,
                TotalStores = devices.Where(d => d.StoreCode > 1).Select(d => d.StoreCode).Distinct().Count(),
                Issues = results.Count,
                Critical = results.Count(r => r.Severity == DiagnosticSeverity.Critical),
                Warning = results.Count(r => r.Severity == DiagnosticSeverity.Warning),
                ByType = results.GroupBy(r => r.Type).Select(g => new { Type = g.Key.ToString(), Count = g.Count() }).ToList()
            };

            return Ok(new { summary, diagnostics = results });
        }

        /// <summary>
        /// Belirli bir magazanin zaman serisi (timeline) verisini dondurur.
        /// </summary>
        [HttpGet("store/{storeCode}/timeline")]
        public async Task<IActionResult> GetStoreTimeline(
            int storeCode,
            [FromQuery] int hours = 24)
        {
            var result = await _diagnostics.GetStoreTimelineAsync(storeCode, hours);
            return Ok(result);
        }

        /// <summary>
        /// Eski durum gecis kayitlarini temizler.
        /// </summary>
        [HttpDelete("purge")]
        public async Task<IActionResult> PurgeOldData([FromQuery] int retainDays = 30)
        {
            var deleted = await _diagnostics.PurgeOldChangesAsync(retainDays);
            _logger.LogInformation("Purged {Count} old status change records (older than {Days} days)", deleted, retainDays);
            return Ok(new { deleted, retainDays });
        }

        // ─── Helper: mevcut cihaz durumunu al (SqlQueryController cache) ───
        private static async Task<List<Models.StoreDeviceWithStatusDto>> GetCurrentDeviceStatusAsync(
            Data.MudoSoftDbContext db,
            FastSqlReachabilityService fastCheck)
        {
            // SqlQueryController'daki static cache'i oku
            var cached = SqlQueryController.GetCachedDevices();
            if (cached != null) return cached;

            // Cache yoksa DB'den al + hizli kontrol
            var devices = await db.StoreDevices
                .AsNoTracking()
                .Select(d => new Models.StoreDeviceWithStatusDto
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

            using var sem = new SemaphoreSlim(40);
            var tasks = devices.Select(async d =>
            {
                await sem.WaitAsync();
                try
                {
                    var isRouter = d.DeviceType.Equals("ROUTER", StringComparison.OrdinalIgnoreCase);
                    var isGecici = d.DeviceType.Equals("gecici", StringComparison.OrdinalIgnoreCase);
                    if (isRouter || isGecici)
                    {
                        using var ping = new System.Net.NetworkInformation.Ping();
                        var reply = await ping.SendPingAsync(d.CalculatedIpAddress, 500);
                        d.IsOnline = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
                    }
                    else
                    {
                        d.IsOnline = await fastCheck.IsSqlReachableAsync(d.CalculatedIpAddress, 1433, 500);
                    }
                }
                catch { d.IsOnline = false; }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);

            return devices;
        }
    }
}
