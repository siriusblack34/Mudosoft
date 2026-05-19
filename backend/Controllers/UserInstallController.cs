using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/user-install")]
public class UserInstallController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly ActivityLogService _activity;
    private readonly ILogger<UserInstallController> _logger;

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(10);

    public UserInstallController(OrchestraDbContext db, ActivityLogService activity, ILogger<UserInstallController> logger)
    {
        _db = db;
        _activity = activity;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        IQueryable<PendingUserInstall> q = _db.PendingUserInstalls.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PendingUserInstallStatus>(status, true, out var st))
            q = q.Where(p => p.Status == st);

        var items = await q
            .OrderByDescending(p => p.RequestedAt)
            .Take(500)
            .ToListAsync(ct);

        return Ok(new { items });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (req?.Users == null || req.Users.Count == 0)
            return BadRequest(new { error = "En az bir kullanıcı gerekli" });

        var requestedBy = User.Identity?.Name;
        var now = DateTime.UtcNow;
        var expiresAt = now + DefaultTtl;
        var created = new List<PendingUserInstall>();
        var reactivated = new List<PendingUserInstall>();

        foreach (var u in req.Users.DistinctBy(x => x.SamAccountName, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(u.SamAccountName)) continue;
            var sam = u.SamAccountName.Trim();

            // Mevcut aktif kayıt varsa reaktive et
            var existing = await _db.PendingUserInstalls
                .Where(p => p.SamAccountName == sam
                            && (p.Status == PendingUserInstallStatus.Waiting
                                || p.Status == PendingUserInstallStatus.Matched
                                || p.Status == PendingUserInstallStatus.Installing))
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                existing.ExpiresAt = expiresAt;
                existing.UpdatedAt = now;
                reactivated.Add(existing);
                continue;
            }

            var entity = new PendingUserInstall
            {
                SamAccountName = sam,
                DisplayName = u.DisplayName,
                RequestedBy = requestedBy,
                RequestedAt = now,
                ExpiresAt = expiresAt,
                Status = PendingUserInstallStatus.Waiting,
                UpdatedAt = now
            };
            _db.PendingUserInstalls.Add(entity);
            created.Add(entity);
        }

        await _db.SaveChangesAsync(ct);

        _ = _activity.LogAsync("UserInstall", "Schedule",
            string.Join(",", created.Concat(reactivated).Select(p => p.SamAccountName)),
            $"Created {created.Count}, reactivated {reactivated.Count}, expires {expiresAt:yyyy-MM-dd}");

        return Ok(new { created = created.Count, reactivated = reactivated.Count, items = created.Concat(reactivated) });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var p = await _db.PendingUserInstalls.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();
        if (p.Status == PendingUserInstallStatus.Done) return BadRequest(new { error = "Tamamlanmış kayıt iptal edilemez" });

        p.Status = PendingUserInstallStatus.Cancelled;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _ = _activity.LogAsync("UserInstall", "Cancel", p.SamAccountName, $"id={id}");
        return Ok(new { ok = true });
    }

    [HttpPost("{id:int}/retry")]
    public async Task<IActionResult> Retry(int id, CancellationToken ct)
    {
        var p = await _db.PendingUserInstalls.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();
        if (p.Status != PendingUserInstallStatus.Failed
            && p.Status != PendingUserInstallStatus.Expired
            && p.Status != PendingUserInstallStatus.Cancelled)
            return BadRequest(new { error = "Yalnızca failed/expired/cancelled kayıt yeniden başlatılabilir" });

        p.Status = PendingUserInstallStatus.Waiting;
        p.LastError = null;
        p.MatchedComputer = null;
        p.MatchedIp = null;
        p.MatchedAt = null;
        p.InstallId = null;
        p.ExpiresAt = DateTime.UtcNow + DefaultTtl;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _ = _activity.LogAsync("UserInstall", "Retry", p.SamAccountName, $"id={id}");
        return Ok(new { ok = true });
    }

    public class CreateRequest
    {
        public List<UserItem> Users { get; set; } = new();
    }
    public class UserItem
    {
        public string SamAccountName { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
    }
}
