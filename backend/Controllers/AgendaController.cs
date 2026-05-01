using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/agenda")]
public class AgendaController : ControllerBase
{
    private static readonly string[] AllowedStatuses = ["Takipte", "Planlandi", "Tamamlandi"];
    private static readonly string[] AllowedPriorities = ["Yuksek", "Orta", "Dusuk"];
    private static readonly string[] AllowedCategories = ["Duyuru", "Altyapi", "Guvenlik", "Magaza Talebi", "Bakim", "Proje"];

    private readonly OrchestraDbContext _db;

    public AgendaController(OrchestraDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AgendaItem>>> GetAgendaItems()
    {
        return await _db.Set<AgendaItem>()
            .AsNoTracking()
            .OrderBy(a => a.Status == "Takipte" ? 0 : a.Status == "Planlandi" ? 1 : 2)
            .ThenBy(a => a.Priority == "Yuksek" ? 0 : a.Priority == "Orta" ? 1 : 2)
            .ThenBy(a => a.DueDate == null ? 1 : 0)
            .ThenBy(a => a.DueDate)
            .ThenByDescending(a => a.UpdatedAt)
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<AgendaItem>> CreateAgendaItem([FromBody] AgendaItemRequest request)
    {
        var validationError = ValidateRequest(request);
        if (validationError != null) return BadRequest(new { error = validationError });

        var now = DateTime.UtcNow;
        var item = new AgendaItem
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Content = request.Content?.Trim() ?? "",
            Status = request.Status,
            Priority = request.Priority,
            Category = request.Category,
            DueDate = ParseDueDate(request.DueDate),
            CreatedBy = GetCurrentDisplayName(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Set<AgendaItem>().Add(item);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAgendaItem), new { id = item.Id }, item);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgendaItem>> GetAgendaItem(Guid id)
    {
        var item = await _db.Set<AgendaItem>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (item == null) return NotFound();
        return item;
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AgendaItem>> UpdateAgendaItem(Guid id, [FromBody] AgendaItemRequest request)
    {
        var validationError = ValidateRequest(request);
        if (validationError != null) return BadRequest(new { error = validationError });

        var item = await _db.Set<AgendaItem>().FirstOrDefaultAsync(a => a.Id == id);
        if (item == null) return NotFound();

        item.Title = request.Title.Trim();
        item.Content = request.Content?.Trim() ?? "";
        item.Status = request.Status;
        item.Priority = request.Priority;
        item.Category = request.Category;
        item.DueDate = ParseDueDate(request.DueDate);
        item.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAgendaItem(Guid id)
    {
        var item = await _db.Set<AgendaItem>().FirstOrDefaultAsync(a => a.Id == id);
        if (item == null) return NotFound();

        _db.Set<AgendaItem>().Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static string? ValidateRequest(AgendaItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return "Baslik zorunludur.";

        if (request.Title.Trim().Length > 200)
            return "Baslik en fazla 200 karakter olabilir.";

        if (!AllowedStatuses.Contains(request.Status))
            return "Gecersiz durum secildi.";

        if (!AllowedPriorities.Contains(request.Priority))
            return "Gecersiz oncelik secildi.";

        if (!AllowedCategories.Contains(request.Category))
            return "Gecersiz kategori secildi.";

        if (!string.IsNullOrWhiteSpace(request.DueDate) && ParseDueDate(request.DueDate) == null)
            return "Termin tarihi gecersiz.";

        return null;
    }

    private static DateTime? ParseDueDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTime.TryParse(value, out var date)) return null;
        return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
    }

    private string GetCurrentDisplayName()
    {
        var fullName = User.FindFirstValue("fullName")?.Trim();
        if (!string.IsNullOrWhiteSpace(fullName)) return fullName;

        var username = User.FindFirstValue(ClaimTypes.Name)?.Trim();
        if (!string.IsNullOrWhiteSpace(username)) return username;

        return "BT";
    }
}

public class AgendaItemRequest
{
    public string Title { get; set; } = "";
    public string? Content { get; set; }
    public string Status { get; set; } = "Takipte";
    public string Priority { get; set; } = "Orta";
    public string Category { get; set; } = "Duyuru";
    public string? DueDate { get; set; }
}
