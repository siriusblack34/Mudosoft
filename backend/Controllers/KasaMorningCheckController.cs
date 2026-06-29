using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/kasa-morning-check")]
public class KasaMorningCheckController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly KasaMorningCheckWorker _worker;
    private readonly ILogger<KasaMorningCheckController> _logger;

    public KasaMorningCheckController(
        OrchestraDbContext db,
        KasaMorningCheckWorker worker,
        ILogger<KasaMorningCheckController> logger)
    {
        _db = db;
        _worker = worker;
        _logger = logger;
    }

    /// <summary>Bugünün sabah kontrolü sonuçlarını getirir.</summary>
    [HttpGet("today")]
    public async Task<IActionResult> GetToday(CancellationToken ct)
    {
        // UTC'de bugünün başlangıcı
        var todayUtc = DateTime.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);

        var results = await _db.KasaMorningChecks
            .AsNoTracking()
            .Where(k => k.CheckedAt >= todayUtc && k.CheckedAt < tomorrowUtc)
            .OrderBy(k => k.StoreCode)
            .ThenBy(k => k.DeviceType)
            .Select(k => new
            {
                k.Id,
                k.StoreDeviceId,
                k.StoreCode,
                k.StoreName,
                k.DeviceType,
                k.IpAddress,
                CheckedAt = k.CheckedAt,
                k.IsUncReachable,
                k.IsGeniusPosLogFound,
                k.IsHealthy,
                k.ErrorMessage,
            })
            .ToListAsync(ct);

        var summary = new
        {
            CheckDate = todayUtc.ToString("yyyy-MM-dd"),
            TotalChecked = results.Count,
            HealthyCount = results.Count(r => r.IsHealthy),
            UnhealthyCount = results.Count(r => !r.IsHealthy),
            HasResults = results.Count > 0,
            Items = results,
        };

        return Ok(summary);
    }

    /// <summary>Son N günün kontrollerini getirir (varsayılan 7 gün).</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int days = 7, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 30);
        var fromUtc = DateTime.UtcNow.Date.AddDays(-days);

        var results = await _db.KasaMorningChecks
            .AsNoTracking()
            .Where(k => k.CheckedAt >= fromUtc)
            .OrderByDescending(k => k.CheckedAt)
            .ThenBy(k => k.StoreCode)
            .Select(k => new
            {
                k.Id,
                k.StoreDeviceId,
                k.StoreCode,
                k.StoreName,
                k.DeviceType,
                k.IpAddress,
                CheckedAt = k.CheckedAt,
                k.IsUncReachable,
                k.IsGeniusPosLogFound,
                k.IsHealthy,
                k.ErrorMessage,
            })
            .ToListAsync(ct);

        return Ok(results);
    }

    /// <summary>Manuel kontrol tetikle (Admin). Normalde 08:00'de otomatik çalışır.</summary>
    [HttpPost("run")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RunNow(CancellationToken ct)
    {
        _logger.LogInformation("Manuel kasa sabah kontrolü tetiklendi: {User}", User.Identity?.Name);

        // Aynı günden daha önce kontrol yapıldıysa sil (yeniden çalıştır)
        var todayUtc = DateTime.UtcNow.Date;
        var existing = _db.KasaMorningChecks.Where(k => k.CheckedAt >= todayUtc);
        _db.KasaMorningChecks.RemoveRange(existing);
        await _db.SaveChangesAsync(ct);

        await _worker.RunCheckAsync(ct);

        // Sonuçları dön
        var results = await _db.KasaMorningChecks
            .AsNoTracking()
            .Where(k => k.CheckedAt >= todayUtc)
            .OrderBy(k => k.StoreCode)
            .ThenBy(k => k.DeviceType)
            .ToListAsync(ct);

        return Ok(new
        {
            message = $"{results.Count} kasa kontrol edildi",
            healthyCount = results.Count(r => r.IsHealthy),
            unhealthyCount = results.Count(r => !r.IsHealthy),
            items = results,
        });
    }
}
