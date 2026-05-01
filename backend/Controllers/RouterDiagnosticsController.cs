using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/router-diagnostics")]
    public class RouterDiagnosticsController : ControllerBase
    {
        private readonly MobileLineDetectionService _detection;
        private readonly OrchestraDbContext _db;

        public RouterDiagnosticsController(MobileLineDetectionService detection, OrchestraDbContext db)
        {
            _detection = detection;
            _db = db;
        }

        /// <summary>Tum magazalarin karasal/mobil hat siniflandirmasi.</summary>
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrent([FromQuery] int windowMinutes = 10)
        {
            var classifications = await _detection.ClassifyAllAsync(windowMinutes);

            var summary = new
            {
                AnalyzedAt = DateTime.UtcNow,
                WindowMinutes = windowMinutes,
                Total = classifications.Count,
                Terrestrial = classifications.Count(c => c.Class == RouterLineClass.Terrestrial),
                MobileSuspected = classifications.Count(c => c.Class == RouterLineClass.MobileSuspected),
                MobileLikely = classifications.Count(c => c.Class == RouterLineClass.MobileLikely),
                Unstable = classifications.Count(c => c.Class == RouterLineClass.Unstable),
                Unknown = classifications.Count(c => c.Class == RouterLineClass.Unknown),
                Switchovers = classifications.Count(c => c.SwitchoverDetected)
            };

            return Ok(new { summary, routers = classifications });
        }

        /// <summary>Bir magazanin router ping gecmisi (zaman serisi).</summary>
        [HttpGet("store/{storeCode}/history")]
        public async Task<IActionResult> GetHistory(int storeCode, [FromQuery] int hours = 24)
        {
            var points = await _detection.GetHistoryAsync(storeCode, hours);
            return Ok(new
            {
                StoreCode = storeCode,
                Hours = hours,
                Points = points
            });
        }

        /// <summary>Son N saatte karasal->mobil gecisi tespit edilen magazalar.</summary>
        [HttpGet("mobile-switchovers")]
        public async Task<IActionResult> GetSwitchovers([FromQuery] int hours = 24)
        {
            // Her bir router icin rolling pencere karsilastirmasi — toplu degil kolay yol:
            // son 10dk'dan classify, SwitchoverDetected olanlari don.
            var classifications = await _detection.ClassifyAllAsync(10);
            var switchovers = classifications
                .Where(c => c.SwitchoverDetected)
                .OrderByDescending(c => c.AvgRttMs)
                .ToList();
            return Ok(switchovers);
        }

        /// <summary>Eski ornekleri siler (default 7 gun).</summary>
        [HttpDelete("purge")]
        public async Task<IActionResult> Purge([FromQuery] int retainDays = 7)
        {
            var deleted = await _detection.PurgeOldSamplesAsync(retainDays);
            return Ok(new { deleted, retainDays });
        }

        /// <summary>Tum magazalarin karasal hat Mbps listesi.</summary>
        [HttpGet("network-info")]
        public async Task<IActionResult> GetNetworkInfo()
        {
            var items = await _db.StoreNetworkInfos.AsNoTracking().OrderBy(s => s.StoreCode).ToListAsync();
            return Ok(items);
        }

        /// <summary>Bir magazanin karasal hat bilgisini gunceller.</summary>
        [HttpPut("network-info/{storeCode}")]
        public async Task<IActionResult> UpdateNetworkInfo(int storeCode, [FromBody] StoreNetworkInfoUpdateDto dto)
        {
            var existing = await _db.StoreNetworkInfos.FirstOrDefaultAsync(s => s.StoreCode == storeCode);
            if (existing == null)
            {
                existing = new StoreNetworkInfo { StoreCode = storeCode };
                _db.StoreNetworkInfos.Add(existing);
            }
            existing.TerrestrialMbps = dto.TerrestrialMbps;
            existing.LineType = dto.LineType;
            existing.Notes = dto.Notes;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(existing);
        }
    }

    public class StoreNetworkInfoUpdateDto
    {
        public int TerrestrialMbps { get; set; }
        public string? LineType { get; set; }
        public string? Notes { get; set; }
    }
}
