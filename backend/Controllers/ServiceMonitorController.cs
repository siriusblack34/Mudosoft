using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/service-monitor")]
public class ServiceMonitorController : ControllerBase
{
    private readonly OrchestraDbContext _db;

    public ServiceMonitorController(OrchestraDbContext db)
    {
        _db = db;
    }

    [HttpGet("incidents/active")]
    public async Task<IActionResult> GetActiveIncidents(CancellationToken ct)
    {
        var incidents = await _db.StoreServiceIncidents
            .AsNoTracking()
            .Where(i => i.ResolvedAt == null)
            .OrderBy(i => i.Severity == "Warning")
            .ThenBy(i => i.StoreCode)
            .ThenBy(i => i.DeviceName)
            .ThenBy(i => i.DisplayName)
            .Select(i => new
            {
                i.Id,
                i.DeviceId,
                i.StoreCode,
                i.StoreName,
                i.DeviceName,
                i.IpAddress,
                i.ServiceName,
                i.DisplayName,
                i.Status,
                i.Severity,
                i.Message,
                i.LastStartMode,
                i.LastError,
                i.ConsecutiveFailures,
                i.FirstDetectedAt,
                i.LastDetectedAt,
                i.ResolvedAt
            })
            .ToListAsync(ct);

        return Ok(incidents);
    }

    [HttpGet("incidents/recent")]
    public async Task<IActionResult> GetRecentIncidents([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        hours = Math.Clamp(hours, 1, 168);
        var since = DateTime.UtcNow.AddHours(-hours);

        var incidents = await _db.StoreServiceIncidents
            .AsNoTracking()
            .Where(i => i.FirstDetectedAt >= since || i.LastDetectedAt >= since || i.ResolvedAt >= since)
            .OrderByDescending(i => i.ResolvedAt == null)
            .ThenByDescending(i => i.LastDetectedAt)
            .Take(200)
            .Select(i => new
            {
                i.Id,
                i.DeviceId,
                i.StoreCode,
                i.StoreName,
                i.DeviceName,
                i.IpAddress,
                i.ServiceName,
                i.DisplayName,
                i.Status,
                i.Severity,
                i.Message,
                i.LastStartMode,
                i.LastError,
                i.ConsecutiveFailures,
                i.FirstDetectedAt,
                i.LastDetectedAt,
                i.ResolvedAt
            })
            .ToListAsync(ct);

        return Ok(incidents);
    }
}
