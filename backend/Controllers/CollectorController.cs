using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;
using Orchestra.Shared.Dtos;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Route("api/agent")]
public class CollectorController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly ILogger<CollectorController> _logger;
    private readonly EventLogTranslationService _translator;
    private readonly EventLogAnalysisService _analysisService;
    private readonly IServiceProvider _services;

    public CollectorController(
        OrchestraDbContext db,
        ILogger<CollectorController> logger,
        EventLogTranslationService translator,
        EventLogAnalysisService analysisService,
        IServiceProvider services)
    {
        _db = db;
        _logger = logger;
        _translator = translator;
        _analysisService = analysisService;
        _services = services;
    }

    [HttpPost("collector-report")]
    [AllowAnonymous]
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

        _logger.LogDebug("Received {Count} collector results from {DeviceId}", entities.Count, report.DeviceId);

        return Ok(new { saved = entities.Count });
    }

    [HttpGet("collector-report/{deviceId}")]
    [Authorize]
    public async Task<IActionResult> GetLatest(string deviceId, [FromQuery] string? collector = null, [FromQuery] int limit = 50)
    {
        var resolvedDeviceId = await ResolveCollectorDeviceIdAsync(deviceId);

        var query = _db.CollectorReports
            .Where(r => r.DeviceId == resolvedDeviceId)
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

    [HttpGet("collector-report/{deviceId}/latest")]
    [Authorize]
    public async Task<IActionResult> GetLatestPerCollector(string deviceId)
    {
        var resolvedDeviceId = await ResolveCollectorDeviceIdAsync(deviceId);

        var results = await _db.CollectorReports
            .FromSqlInterpolated($@"
                SELECT DISTINCT ON (""CollectorName"") *
                FROM ""CollectorReports""
                WHERE ""DeviceId"" = {resolvedDeviceId}
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

    [HttpGet("collector-report/{deviceId}/eventlogs")]
    [Authorize]
    public async Task<IActionResult> GetEventLogs(string deviceId, [FromQuery] int limit = 100)
    {
        var resolvedDeviceId = await ResolveCollectorDeviceIdAsync(deviceId);

        var reports = await _db.CollectorReports
            .Where(r => r.DeviceId == resolvedDeviceId && r.CollectorName == "EventLog")
            .OrderByDescending(r => r.TimestampUtc)
            .Take(20)
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
            catch
            {
            }
        }

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

    [HttpGet("collector-report/{deviceId}/eventlogs/analysis")]
    [Authorize]
    public async Task<IActionResult> GetEventLogAnalysis(string deviceId, [FromQuery] int hours = 24, [FromQuery] int limit = 200)
    {
        var resolvedDeviceId = await ResolveCollectorDeviceIdAsync(deviceId);
        var analysis = await _analysisService.AnalyzeAsync(resolvedDeviceId, hours, limit);
        return Ok(analysis);
    }

    /// <summary>
    /// Hedef cihazin event log'unu backend'den dogrudan RPC ile ceker (agent gerekmez).
    /// MudoSoft:Wmi domain credentials kullanir. Hem agentless cihazlar hem de fresh pull icin.
    /// </summary>
    [HttpPost("collector-report/{deviceId}/eventlogs/pull")]
    [Authorize]
    public async Task<IActionResult> PullEventLogs(string deviceId, [FromQuery] int hours = 24, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return StatusCode(503, new { error = "Bu islem yalnizca Windows backend'de calisir." });

        var pullService = _services.GetService<RemoteEventLogPullService>();
        if (pullService == null)
            return StatusCode(503, new { error = "RemoteEventLogPullService aktif degil." });

        var result = await pullService.PullAsync(deviceId, hours, ct);
        if (!result.Success)
            return StatusCode(502, new { error = result.ErrorMessage, host = result.Host });

        var analysis = await _analysisService.AnalyzeAsync(result.StoredAsDeviceId ?? deviceId, hours, 300);

        return Ok(new
        {
            host = result.Host,
            eventCount = result.EventCount,
            storedAsDeviceId = result.StoredAsDeviceId,
            partialErrors = result.PartialErrors,
            analysis
        });
    }

    private async Task<string> ResolveCollectorDeviceIdAsync(string requestedDeviceId)
    {
        if (string.IsNullOrWhiteSpace(requestedDeviceId))
            return requestedDeviceId;

        var hasDirectCollectorData = await _db.CollectorReports
            .AsNoTracking()
            .AnyAsync(r => r.DeviceId == requestedDeviceId);
        if (hasDirectCollectorData)
            return requestedDeviceId;

        var exactAgentDevice = await _db.Devices
            .AsNoTracking()
            .AnyAsync(d => d.Id == requestedDeviceId);
        if (exactAgentDevice)
            return requestedDeviceId;

        var storeDevice = await _db.StoreDevices
            .AsNoTracking()
            .Where(sd => sd.DeviceId == requestedDeviceId)
            .Select(sd => new { sd.CalculatedIpAddress })
            .FirstOrDefaultAsync();
        if (storeDevice == null || string.IsNullOrWhiteSpace(storeDevice.CalculatedIpAddress))
            return requestedDeviceId;

        var matchedAgentDeviceId = await _db.Devices
            .AsNoTracking()
            .Where(d => d.IpAddress == storeDevice.CalculatedIpAddress)
            .OrderByDescending(d => d.LastSeen)
            .Select(d => d.Id)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(matchedAgentDeviceId)
            ? requestedDeviceId
            : matchedAgentDeviceId;
    }
}
