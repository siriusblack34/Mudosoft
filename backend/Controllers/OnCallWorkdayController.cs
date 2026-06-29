using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/oncall-workdays")]
public class OnCallWorkdayController : ControllerBase
{
    private static readonly string[] AllowedDayTypes = ["ResmiTatil", "HaftaSonu", "Mesai"];

    private readonly OrchestraDbContext _db;

    public OnCallWorkdayController(OrchestraDbContext db)
    {
        _db = db;
    }

    // GET /api/oncall-workdays?year=2024&month=6
    // Admin: tüm kullanıcılar. Teknisyen: sadece kendisi.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OnCallWorkdayDto>>> GetWorkdays(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] int? userId)
    {
        var y = year ?? DateTime.Now.Year;
        var m = month ?? DateTime.Now.Month;

        var from = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);

        var query = _db.OnCallWorkdays
            .AsNoTracking()
            .Where(w => w.WorkDate >= from && w.WorkDate < to);

        // Non-admin kullanıcı sadece kendisini görebilir
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin)
        {
            var currentUsername = User.FindFirstValue(ClaimTypes.Name) ?? "";
            query = query.Where(w => w.Username == currentUsername);
        }
        else if (userId.HasValue)
        {
            query = query.Where(w => w.UserId == userId.Value);
        }

        var workdays = await query
            .OrderBy(w => w.WorkDate)
            .Select(w => new OnCallWorkdayDto
            {
                Id = w.Id,
                UserId = w.UserId,
                Username = w.Username,
                FullName = w.FullName,
                WorkDate = w.WorkDate,
                DayType = w.DayType,
                Notes = w.Notes,
                CreatedAt = w.CreatedAt
            })
            .ToListAsync();

        return Ok(workdays);
    }

    // GET /api/oncall-workdays/summary?year=2024&month=6
    // Tüm personelin o ay özeti (admin görünümü)
    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<UserWorkdaySummaryDto>>> GetMonthlySummary(
        [FromQuery] int? year,
        [FromQuery] int? month)
    {
        var y = year ?? DateTime.Now.Year;
        var m = month ?? DateTime.Now.Month;

        var from = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);

        var workdays = await _db.OnCallWorkdays
            .AsNoTracking()
            .Where(w => w.WorkDate >= from && w.WorkDate < to)
            .ToListAsync();

        var summary = workdays
            .GroupBy(w => new { w.UserId, w.Username, w.FullName })
            .Select(g => new UserWorkdaySummaryDto
            {
                UserId = g.Key.UserId,
                Username = g.Key.Username,
                FullName = g.Key.FullName,
                TotalDays = g.Count(),
                ResmiTatilCount = g.Count(w => w.DayType == "ResmiTatil"),
                HaftaSonuCount = g.Count(w => w.DayType == "HaftaSonu"),
                MesaiCount = g.Count(w => w.DayType == "Mesai"),
                WorkDates = g.Select(w => w.WorkDate).OrderBy(d => d).ToList()
            })
            .OrderBy(s => s.FullName)
            .ToList();

        return Ok(summary);
    }

    // POST /api/oncall-workdays
    [HttpPost]
    public async Task<ActionResult<OnCallWorkdayDto>> CreateWorkday([FromBody] OnCallWorkdayRequest request)
    {
        var err = ValidateRequest(request);
        if (err != null) return BadRequest(new { error = err });

        var (userId, username, fullName) = GetCurrentUser();

        // Kullanıcı sadece kendisi adına kayıt açabilir (admin hariç)
        var isAdmin = User.IsInRole("Admin");
        var targetUsername = isAdmin && !string.IsNullOrWhiteSpace(request.Username)
            ? request.Username
            : username;

        var targetUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == targetUsername);

        if (targetUser == null) return BadRequest(new { error = "Kullanıcı bulunamadı." });

        var workDate = ParseDate(request.WorkDate)!.Value;

        // Aynı gün için çift kayıt engeli
        var existing = await _db.OnCallWorkdays
            .FirstOrDefaultAsync(w => w.UserId == targetUser.Id && w.WorkDate == workDate);
        if (existing != null) return Conflict(new { error = "Bu kullanıcı için bu gün zaten kayıtlı." });

        var workday = new OnCallWorkday
        {
            UserId = targetUser.Id,
            Username = targetUser.Username,
            FullName = targetUser.FullName,
            WorkDate = workDate,
            DayType = request.DayType,
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.OnCallWorkdays.Add(workday);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetWorkdays), new { }, ToDto(workday));
    }

    // DELETE /api/oncall-workdays/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteWorkday(int id)
    {
        var workday = await _db.OnCallWorkdays.FirstOrDefaultAsync(w => w.Id == id);
        if (workday == null) return NotFound();

        // Sadece kendi kaydını silebilir (admin hariç)
        var (_, username, _) = GetCurrentUser();
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && workday.Username != username)
            return Forbid();

        _db.OnCallWorkdays.Remove(workday);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // PUT /api/oncall-workdays/{id}
    [HttpPut("{id:int}")]
    public async Task<ActionResult<OnCallWorkdayDto>> UpdateWorkday(int id, [FromBody] OnCallWorkdayRequest request)
    {
        var err = ValidateRequest(request);
        if (err != null) return BadRequest(new { error = err });

        var workday = await _db.OnCallWorkdays.FirstOrDefaultAsync(w => w.Id == id);
        if (workday == null) return NotFound();

        var (_, username, _) = GetCurrentUser();
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && workday.Username != username) return Forbid();

        workday.DayType = request.DayType;
        workday.Notes = request.Notes?.Trim();

        await _db.SaveChangesAsync();
        return Ok(ToDto(workday));
    }

    private static string? ValidateRequest(OnCallWorkdayRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.WorkDate)) return "Tarih zorunludur.";
        if (ParseDate(req.WorkDate) == null) return "Geçersiz tarih formatı.";
        if (!AllowedDayTypes.Contains(req.DayType)) return "Geçersiz gün türü.";
        return null;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTime.TryParse(value, out var d)) return null;
        return DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
    }

    private (int id, string username, string fullName) GetCurrentUser()
    {
        var username = User.FindFirstValue(ClaimTypes.Name) ?? "";
        var fullName = User.FindFirstValue("fullName") ?? username;
        _ = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id);
        return (id, username, fullName);
    }

    private static OnCallWorkdayDto ToDto(OnCallWorkday w) => new()
    {
        Id = w.Id,
        UserId = w.UserId,
        Username = w.Username,
        FullName = w.FullName,
        WorkDate = w.WorkDate,
        DayType = w.DayType,
        Notes = w.Notes,
        CreatedAt = w.CreatedAt
    };
}

public class OnCallWorkdayRequest
{
    public string WorkDate { get; set; } = "";
    public string DayType { get; set; } = "ResmiTatil";
    public string? Notes { get; set; }
    public string? Username { get; set; } // Sadece admin kullanır
}

public class OnCallWorkdayDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public DateTime WorkDate { get; set; }
    public string DayType { get; set; } = "";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserWorkdaySummaryDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public int TotalDays { get; set; }
    public int ResmiTatilCount { get; set; }
    public int HaftaSonuCount { get; set; }
    public int MesaiCount { get; set; }
    public List<DateTime> WorkDates { get; set; } = [];
}
