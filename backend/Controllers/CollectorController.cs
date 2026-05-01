using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;
using Orchestra.Shared.Dtos;
using System.Text.Json;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Route("api/agent")]
public class CollectorController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly ILogger<CollectorController> _logger;
    private readonly EventLogTranslationService _translator;

    public CollectorController(OrchestraDbContext db, ILogger<CollectorController> logger, EventLogTranslationService translator)
    {
        _db = db;
        _logger = logger;
        _translator = translator;
    }

    /// <summary>
    /// Agent'lar collector sonuçlarını bu endpoint'e POST eder.
    /// </summary>
    [HttpPost("collector-report")]
    [AllowAnonymous] // Agent token ile doğrulama ileride eklenecek
    public async Task<IActionResult> ReceiveReport([FromBody] CollectorReportDto report)
    {
        if (report.Results == null || report.Results.Count == 0)
            return BadRequest("No results in report");

        var entities = report.Results.Select(r => new CollectorReport
        {
            DeviceId = report.DeviceId,
            CollectorName = r.CollectorName,
            TimestampUtc = r.TimestampUtc,
            Severity = r.Severity,
            JsonData = r.JsonData,
            Success = r.Success,
            ErrorMessage = r.ErrorMessage
        }).ToList();

        _db.CollectorReports.AddRange(entities);
        await _db.SaveChangesAsync();

        _logger.LogDebug("Received {Count} collector results from {DeviceId}",
            entities.Count, report.DeviceId);

        return Ok(new { saved = entities.Count });
    }

    /// <summary>
    /// Belirli bir cihazın son collector verilerini döndürür.
    /// </summary>
    [HttpGet("collector-report/{deviceId}")]
    [Authorize]
    public async Task<IActionResult> GetLatest(string deviceId, [FromQuery] string? collector = null, [FromQuery] int limit = 50)
    {
        var query = _db.CollectorReports
            .Where(r => r.DeviceId == deviceId)
            .AsQueryable();

        if (!string.IsNullOrEmpty(collector))
            query = query.Where(r => r.CollectorName == collector);

        var results = await query
            .OrderByDescending(r => r.TimestampUtc)
            .Take(limit)
            .Select(r => new
            {
                r.CollectorName,
                r.TimestampUtc,
                r.Severity,
                r.JsonData,
                r.Success,
                r.ErrorMessage
            })
            .ToListAsync();

        return Ok(results);
    }

    /// <summary>
    /// Belirli bir cihazın her collector için son sonucunu döndürür.
    /// </summary>
    [HttpGet("collector-report/{deviceId}/latest")]
    [Authorize]
    public async Task<IActionResult> GetLatestPerCollector(string deviceId)
    {
        // Raw SQL ile PostgreSQL DISTINCT ON kullanarak her collector için son kaydı çek
        var results = await _db.CollectorReports
            .FromSqlInterpolated($@"
                SELECT DISTINCT ON (""CollectorName"") *
                FROM ""CollectorReports""
                WHERE ""DeviceId"" = {deviceId}
                ORDER BY ""CollectorName"", ""TimestampUtc"" DESC")
            .Select(r => new
            {
                r.CollectorName,
                r.TimestampUtc,
                r.Severity,
                r.JsonData,
                r.Success,
                r.ErrorMessage
            })
            .ToListAsync();

        return Ok(results);
    }

    /// <summary>
    /// Belirli bir cihazın EventLog verilerini Türkçe çeviri ile döndürür.
    /// </summary>
    [HttpGet("collector-report/{deviceId}/eventlogs")]
    [Authorize]
    public async Task<IActionResult> GetEventLogs(string deviceId, [FromQuery] int limit = 100)
    {
        var reports = await _db.CollectorReports
            .Where(r => r.DeviceId == deviceId && r.CollectorName == "EventLog")
            .OrderByDescending(r => r.TimestampUtc)
            .Take(20) // Son 20 rapor (her biri 50 event içerebilir)
            .Select(r => r.JsonData)
            .ToListAsync();

        var allEntries = new List<EventLogEntryDto>();
        foreach (var json in reports)
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<EventLogEntryDto>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (entries != null)
                    allEntries.AddRange(entries);
            }
            catch { }
        }

        // Çeviri uygula ve sırala
        var result = allEntries
            .OrderByDescending(e => e.TimeGenerated)
            .Take(limit)
            .Select(e =>
            {
                var (translated, action) = _translator.Translate(e.Source, e.EventId);
                e.TranslatedMessage = translated;
                e.SuggestedAction = action;
                return e;
            })
            .ToList();

        return Ok(result);
    }
}
