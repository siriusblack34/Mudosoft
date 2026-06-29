using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/shift-handover")]
public class ShiftHandoverController : ControllerBase
{
    private readonly OrchestraDbContext _db;

    public ShiftHandoverController(OrchestraDbContext db)
    {
        _db = db;
    }

    // GET /api/shift-handover?from=2024-01-15T22:00:00&to=2024-01-16T08:00:00
    [HttpGet]
    public async Task<ActionResult<ShiftHandoverDto>> GetHandover(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        if (from >= to) return BadRequest(new { error = "Başlangıç tarihi bitiş tarihinden önce olmalı." });
        if ((to - from).TotalHours > 24) return BadRequest(new { error = "Maksimum 24 saatlik dönem seçilebilir." });

        var fromUtc = from.Kind == DateTimeKind.Utc ? from : from.ToUniversalTime();
        var toUtc = to.Kind == DateTimeKind.Utc ? to : to.ToUniversalTime();

        // Mağaza offline olayları
        var offlineLogs = await _db.StoreOfflineLogs
            .Where(l => l.OfflineAt >= fromUtc && l.OfflineAt < toUtc)
            .OrderBy(l => l.OfflineAt)
            .ToListAsync();

        var offlineEvents = offlineLogs.Select(l => new OfflineEventDto
        {
            StoreCode = l.StoreCode.ToString(),
            StoreName = l.StoreName ?? "",
            OfflineAt = l.OfflineAt.ToLocalTime(),
            OnlineAt = l.OnlineAt.HasValue ? l.OnlineAt.Value.ToLocalTime() : null,
            DurationMinutes = l.OnlineAt.HasValue
                ? (int)(l.OnlineAt.Value - l.OfflineAt).TotalMinutes
                : (int)(toUtc - l.OfflineAt).TotalMinutes,
            IsStillOffline = !l.OnlineAt.HasValue
        }).ToList();

        // Servis kesintileri
        var incidentLogs = await _db.StoreServiceIncidents
            .Where(i => i.FirstDetectedAt >= fromUtc && i.FirstDetectedAt < toUtc)
            .OrderBy(i => i.FirstDetectedAt)
            .ToListAsync();

        var serviceIncidents = incidentLogs.Select(i => new ServiceIncidentDto
        {
            StoreCode = i.StoreCode.ToString(),
            StoreName = i.StoreName,
            DeviceName = i.DeviceName,
            ServiceName = i.DisplayName,
            Severity = i.Severity,
            FirstDetectedAt = i.FirstDetectedAt.ToLocalTime(),
            ResolvedAt = i.ResolvedAt.HasValue ? i.ResolvedAt.Value.ToLocalTime() : null,
            IsResolved = i.ResolvedAt.HasValue
        }).ToList();

        // Cihaz durum değişiklikleri — DeviceStatusChange: StoreCode int, IsOnline bool, DeviceType string
        var statusLogs = await _db.DeviceStatusChanges
            .Where(c => c.ChangedAt >= fromUtc && c.ChangedAt < toUtc)
            .OrderByDescending(c => c.ChangedAt)
            .Take(200)
            .ToListAsync();

        var statusChanges = statusLogs.Select(c => new StatusChangeDto
        {
            StoreCode = c.StoreCode.ToString(),
            DeviceType = c.DeviceType,
            WentOnline = c.IsOnline,
            ChangedAt = c.ChangedAt.ToLocalTime()
        }).ToList();

        // Yapılan işlemler — ActivityLog: Action, Target, Details alanları var
        var activityLogs = await _db.ActivityLogs
            .Where(a => a.CreatedAt >= fromUtc && a.CreatedAt < toUtc)
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync();

        var actions = activityLogs.Select(a => new ActionSummaryDto
        {
            Username = a.Username ?? "",
            Category = a.Category,
            Description = a.Action + (a.Target != null ? $" → {a.Target}" : ""),
            PerformedAt = a.CreatedAt.ToLocalTime()
        }).ToList();

        // Özet istatistikler
        var totalOfflineDuration = offlineEvents.Sum(e => e.DurationMinutes);
        var uniqueStoresAffected = offlineEvents.Select(e => e.StoreCode).Distinct().Count();
        var criticalIncidents = serviceIncidents.Count(i => i.Severity == "Critical");

        var summary = new ShiftSummaryDto
        {
            TotalOfflineEvents = offlineEvents.Count,
            UniqueStoresAffected = uniqueStoresAffected,
            TotalOfflineDurationMinutes = totalOfflineDuration,
            StillOfflineCount = offlineEvents.Count(e => e.IsStillOffline),
            ServiceIncidentCount = serviceIncidents.Count(),
            CriticalIncidentCount = criticalIncidents,
            TotalActionsPerformed = actions.Count
        };

        return Ok(new ShiftHandoverDto
        {
            PeriodFrom = from,
            PeriodTo = to,
            GeneratedAt = DateTime.Now,
            Summary = summary,
            OfflineEvents = offlineEvents,
            ServiceIncidents = serviceIncidents,
            StatusChanges = statusChanges,
            Actions = actions
        });
    }
}

public class ShiftHandoverDto
{
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public DateTime GeneratedAt { get; set; }
    public ShiftSummaryDto Summary { get; set; } = new();
    public List<OfflineEventDto> OfflineEvents { get; set; } = [];
    public List<ServiceIncidentDto> ServiceIncidents { get; set; } = [];
    public List<StatusChangeDto> StatusChanges { get; set; } = [];
    public List<ActionSummaryDto> Actions { get; set; } = [];
}

public class ShiftSummaryDto
{
    public int TotalOfflineEvents { get; set; }
    public int UniqueStoresAffected { get; set; }
    public int TotalOfflineDurationMinutes { get; set; }
    public int StillOfflineCount { get; set; }
    public int ServiceIncidentCount { get; set; }
    public int CriticalIncidentCount { get; set; }
    public int TotalActionsPerformed { get; set; }
}

public class OfflineEventDto
{
    public string StoreCode { get; set; } = "";
    public string StoreName { get; set; } = "";
    public DateTime OfflineAt { get; set; }
    public DateTime? OnlineAt { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsStillOffline { get; set; }
}

public class ServiceIncidentDto
{
    public string StoreCode { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string Severity { get; set; } = "";
    public DateTime FirstDetectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool IsResolved { get; set; }
}

public class StatusChangeDto
{
    public string StoreCode { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public bool WentOnline { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class ActionSummaryDto
{
    public string Username { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime PerformedAt { get; set; }
}
