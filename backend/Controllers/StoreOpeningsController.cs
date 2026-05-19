using System.Security.Claims;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/store-openings")]
public class StoreOpeningsController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly ILogger<StoreOpeningsController> _logger;
    private readonly IWebHostEnvironment _env;

    public StoreOpeningsController(OrchestraDbContext db, ILogger<StoreOpeningsController> logger, IWebHostEnvironment env)
    {
        _db = db;
        _logger = logger;
        _env = env;
    }

    // ============================================================
    // DTOs
    // ============================================================
    public record StoreOpeningListDto(
        int Id,
        int StoreCode,
        string StoreName,
        string? City,
        DateTime TargetOpeningDate,
        DateTime? ActualOpeningDate,
        string Status,
        int TotalItems,
        int CompletedItems,
        int NotApplicableItems,
        double ProgressPercent,
        DateTime CreatedAt,
        string? CreatedBy,
        bool IsOverdue
    );

    public record StoreOpeningCreateDto(
        int StoreCode,
        string StoreName,
        string? City,
        string? Address,
        DateTime TargetOpeningDate,
        int? TemplateId,
        string? Notes,
        Dictionary<string, string>? RoleAssignments
    );

    public record StoreOpeningUpdateDto(
        string? StoreName,
        string? City,
        string? Address,
        DateTime? TargetOpeningDate,
        DateTime? ActualOpeningDate,
        string? Status,
        string? Notes,
        Dictionary<string, string>? RoleAssignments
    );

    public record ItemUpdateDto(
        string? Status,
        string? SerialNumber,
        string? AssetNumber,
        string? Notes
    );

    public record AddItemDto(
        string CategoryName,
        string? AssignedRole,
        string ItemName,
        string? ParentName,
        bool HasSerialNumber,
        bool HasAssetNumber,
        int? SortOrder
    );

    public record StoreOpeningDetailDto(
        int Id,
        int StoreCode,
        string StoreName,
        string? City,
        string? Address,
        DateTime TargetOpeningDate,
        DateTime? ActualOpeningDate,
        string Status,
        int? TemplateId,
        string? Notes,
        Dictionary<string, string> RoleAssignments,
        string? CreatedBy,
        DateTime CreatedAt,
        string? UpdatedBy,
        DateTime? UpdatedAt,
        string? CompletedBy,
        DateTime? CompletedAt,
        List<CategoryGroupDto> Categories,
        double ProgressPercent
    );

    public record CategoryGroupDto(
        string CategoryName,
        string? AssignedRole,
        int TotalItems,
        int CompletedItems,
        int NotApplicableItems,
        double ProgressPercent,
        List<StoreOpeningItem> Items
    );

    public record ActivityDto(int Id, string Username, string Action, string? Details, DateTime CreatedAt);

    // ============================================================
    // LIST
    // ============================================================
    [HttpGet]
    public async Task<ActionResult<List<StoreOpeningListDto>>> List([FromQuery] string? status = null)
    {
        var query = _db.StoreOpenings.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status == status);

        var openings = await query
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.StoreCode,
                o.StoreName,
                o.City,
                o.TargetOpeningDate,
                o.ActualOpeningDate,
                o.Status,
                o.CreatedAt,
                o.CreatedBy,
                Total = o.Items.Count,
                Completed = o.Items.Count(i => i.Status == "Completed"),
                NotApplicable = o.Items.Count(i => i.Status == "NotApplicable")
            })
            .ToListAsync();

        var today = DateTime.UtcNow.Date;
        var result = openings.Select(o =>
        {
            var applicable = o.Total - o.NotApplicable;
            var pct = applicable == 0 ? 0 : Math.Round(100.0 * o.Completed / applicable, 1);
            var overdue = o.Status != "Completed" && o.Status != "Cancelled" && o.TargetOpeningDate.Date < today;
            return new StoreOpeningListDto(o.Id, o.StoreCode, o.StoreName, o.City, o.TargetOpeningDate, o.ActualOpeningDate, o.Status, o.Total, o.Completed, o.NotApplicable, pct, o.CreatedAt, o.CreatedBy, overdue);
        }).ToList();

        return Ok(result);
    }

    // ============================================================
    // GET DETAIL
    // ============================================================
    [HttpGet("{id:int}")]
    public async Task<ActionResult<StoreOpeningDetailDto>> Get(int id)
    {
        var opening = await _db.StoreOpenings
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (opening == null) return NotFound();

        var roleAssignments = ParseRoles(opening.RoleAssignmentsJson);

        var categories = opening.Items
            .GroupBy(i => i.CategoryName)
            .OrderBy(g => g.Min(i => i.SortOrder))
            .Select(g =>
            {
                var items = g.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
                var total = items.Count;
                var completed = items.Count(i => i.Status == "Completed");
                var na = items.Count(i => i.Status == "NotApplicable");
                var applicable = total - na;
                var pct = applicable == 0 ? 0 : Math.Round(100.0 * completed / applicable, 1);
                return new CategoryGroupDto(g.Key, items.FirstOrDefault()?.AssignedRole, total, completed, na, pct, items);
            })
            .ToList();

        var totalAll = opening.Items.Count;
        var compAll = opening.Items.Count(i => i.Status == "Completed");
        var naAll = opening.Items.Count(i => i.Status == "NotApplicable");
        var applicableAll = totalAll - naAll;
        var pctAll = applicableAll == 0 ? 0 : Math.Round(100.0 * compAll / applicableAll, 1);

        return Ok(new StoreOpeningDetailDto(
            opening.Id, opening.StoreCode, opening.StoreName, opening.City, opening.Address,
            opening.TargetOpeningDate, opening.ActualOpeningDate, opening.Status, opening.TemplateId,
            opening.Notes, roleAssignments, opening.CreatedBy, opening.CreatedAt,
            opening.UpdatedBy, opening.UpdatedAt, opening.CompletedBy, opening.CompletedAt,
            categories, pctAll));
    }

    // ============================================================
    // CREATE  (uses template if specified, otherwise default template)
    // ============================================================
    [HttpPost]
    public async Task<ActionResult<StoreOpeningDetailDto>> Create([FromBody] StoreOpeningCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.StoreName)) return BadRequest(new { error = "StoreName zorunlu" });

        var templateId = dto.TemplateId;
        StoreOpeningTemplate? template = null;
        if (templateId.HasValue)
        {
            template = await _db.StoreOpeningTemplates.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == templateId.Value);
            if (template == null) return BadRequest(new { error = "Template bulunamadı" });
        }
        else
        {
            template = await _db.StoreOpeningTemplates.Include(t => t.Items).FirstOrDefaultAsync(t => t.IsDefault);
        }

        var username = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "system";

        var opening = new StoreOpening
        {
            StoreCode = dto.StoreCode,
            StoreName = dto.StoreName.Trim(),
            City = dto.City?.Trim(),
            Address = dto.Address?.Trim(),
            TargetOpeningDate = dto.TargetOpeningDate,
            Status = "Planned",
            TemplateId = template?.Id,
            Notes = dto.Notes,
            RoleAssignmentsJson = dto.RoleAssignments != null ? JsonSerializer.Serialize(dto.RoleAssignments) : null,
            CreatedBy = username,
            CreatedAt = DateTime.UtcNow
        };

        if (template != null)
        {
            foreach (var ti in template.Items.OrderBy(i => i.SortOrder))
            {
                opening.Items.Add(new StoreOpeningItem
                {
                    CategoryName = ti.CategoryName,
                    AssignedRole = ti.AssignedRole,
                    ItemName = ti.ItemName,
                    ParentName = ti.ParentName,
                    HasSerialNumber = ti.HasSerialNumber,
                    HasAssetNumber = ti.HasAssetNumber,
                    SortOrder = ti.SortOrder,
                    Status = "Pending"
                });
            }
        }

        _db.StoreOpenings.Add(opening);
        await _db.SaveChangesAsync();

        _db.StoreOpeningActivities.Add(new StoreOpeningActivity
        {
            StoreOpeningId = opening.Id,
            Username = username,
            Action = "OpeningCreated",
            Details = $"{opening.StoreCode} - {opening.StoreName}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await Get(opening.Id);
    }

    // ============================================================
    // UPDATE meta
    // ============================================================
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] StoreOpeningUpdateDto dto)
    {
        var opening = await _db.StoreOpenings.FindAsync(id);
        if (opening == null) return NotFound();

        var username = User.FindFirstValue(ClaimTypes.Name) ?? "system";

        if (dto.StoreName != null) opening.StoreName = dto.StoreName.Trim();
        if (dto.City != null) opening.City = dto.City.Trim();
        if (dto.Address != null) opening.Address = dto.Address.Trim();
        if (dto.TargetOpeningDate.HasValue) opening.TargetOpeningDate = dto.TargetOpeningDate.Value;
        if (dto.ActualOpeningDate.HasValue) opening.ActualOpeningDate = dto.ActualOpeningDate.Value;
        if (dto.Notes != null) opening.Notes = dto.Notes;
        if (dto.RoleAssignments != null)
            opening.RoleAssignmentsJson = JsonSerializer.Serialize(dto.RoleAssignments);

        if (!string.IsNullOrWhiteSpace(dto.Status) && dto.Status != opening.Status)
        {
            opening.Status = dto.Status;
            if (dto.Status == "Completed")
            {
                opening.CompletedAt = DateTime.UtcNow;
                opening.CompletedBy = username;
                opening.ActualOpeningDate ??= DateTime.UtcNow;
            }
        }

        opening.UpdatedAt = DateTime.UtcNow;
        opening.UpdatedBy = username;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ============================================================
    // DELETE
    // ============================================================
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var opening = await _db.StoreOpenings.FindAsync(id);
        if (opening == null) return NotFound();
        _db.StoreOpenings.Remove(opening);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ============================================================
    // ITEM: UPDATE  (status, serial, asset, notes)
    // ============================================================
    [HttpPut("{openingId:int}/items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(int openingId, int itemId, [FromBody] ItemUpdateDto dto)
    {
        var item = await _db.StoreOpeningItems.FirstOrDefaultAsync(i => i.Id == itemId && i.StoreOpeningId == openingId);
        if (item == null) return NotFound();

        var username = User.FindFirstValue(ClaimTypes.Name) ?? "system";

        if (dto.SerialNumber != null) item.SerialNumber = dto.SerialNumber.Trim();
        if (dto.AssetNumber != null) item.AssetNumber = dto.AssetNumber.Trim();
        if (dto.Notes != null) item.Notes = dto.Notes;

        if (!string.IsNullOrWhiteSpace(dto.Status) && dto.Status != item.Status)
        {
            var prev = item.Status;
            item.Status = dto.Status;
            if (dto.Status == "Completed")
            {
                item.CompletedAt = DateTime.UtcNow;
                item.CompletedBy = username;
            }
            else
            {
                item.CompletedAt = null;
                item.CompletedBy = null;
            }

            _db.StoreOpeningActivities.Add(new StoreOpeningActivity
            {
                StoreOpeningId = openingId,
                Username = username,
                Action = dto.Status == "Completed" ? "ItemCompleted" : "ItemReopened",
                Details = $"{item.CategoryName} / {item.ItemName} ({prev}→{dto.Status})",
                CreatedAt = DateTime.UtcNow
            });
        }

        // If opening was Planned and we just completed an item, bump to InProgress.
        var opening = await _db.StoreOpenings.FindAsync(openingId);
        if (opening != null && opening.Status == "Planned" && item.Status == "Completed")
        {
            opening.Status = "InProgress";
            opening.UpdatedAt = DateTime.UtcNow;
            opening.UpdatedBy = username;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ============================================================
    // ITEM: ADD (ad-hoc — şablon dışı kalem)
    // ============================================================
    [HttpPost("{openingId:int}/items")]
    public async Task<ActionResult<StoreOpeningItem>> AddItem(int openingId, [FromBody] AddItemDto dto)
    {
        var opening = await _db.StoreOpenings.FindAsync(openingId);
        if (opening == null) return NotFound();

        var maxSort = await _db.StoreOpeningItems
            .Where(i => i.StoreOpeningId == openingId && i.CategoryName == dto.CategoryName)
            .Select(i => (int?)i.SortOrder)
            .MaxAsync() ?? 0;

        var item = new StoreOpeningItem
        {
            StoreOpeningId = openingId,
            CategoryName = dto.CategoryName.Trim(),
            AssignedRole = dto.AssignedRole,
            ItemName = dto.ItemName.Trim(),
            ParentName = dto.ParentName?.Trim(),
            HasSerialNumber = dto.HasSerialNumber,
            HasAssetNumber = dto.HasAssetNumber,
            SortOrder = dto.SortOrder ?? maxSort + 10,
            Status = "Pending"
        };
        _db.StoreOpeningItems.Add(item);
        await _db.SaveChangesAsync();
        return Ok(item);
    }

    // ============================================================
    // ITEM: DELETE
    // ============================================================
    [HttpDelete("{openingId:int}/items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int openingId, int itemId)
    {
        var item = await _db.StoreOpeningItems.FirstOrDefaultAsync(i => i.Id == itemId && i.StoreOpeningId == openingId);
        if (item == null) return NotFound();
        _db.StoreOpeningItems.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ============================================================
    // ITEM PHOTO UPLOAD
    // ============================================================
    [HttpPost("{openingId:int}/items/{itemId:int}/photo")]
    [RequestSizeLimit(20 * 1024 * 1024)] // 20MB
    public async Task<IActionResult> UploadPhoto(int openingId, int itemId, IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "Dosya boş" });
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext)) return BadRequest(new { error = "Sadece görsel dosya kabul edilir" });

        var item = await _db.StoreOpeningItems.FirstOrDefaultAsync(i => i.Id == itemId && i.StoreOpeningId == openingId);
        if (item == null) return NotFound();

        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "uploads", "store-openings", openingId.ToString());
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"item-{itemId}-{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
        var fullPath = Path.Combine(uploadsDir, fileName);
        await using (var fs = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(fs);
        }

        // Delete previous photo if exists
        if (!string.IsNullOrEmpty(item.PhotoUrl))
        {
            var oldPath = Path.Combine(Directory.GetCurrentDirectory(), item.PhotoUrl.TrimStart('/'));
            try { if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath); } catch { }
        }

        item.PhotoUrl = $"/uploads/store-openings/{openingId}/{fileName}";
        await _db.SaveChangesAsync();
        return Ok(new { photoUrl = item.PhotoUrl });
    }

    [HttpGet("{openingId:int}/items/{itemId:int}/photo")]
    [AllowAnonymous] // token query param ile çağırılabilsin diye — yine de path doğrulanıyor
    public async Task<IActionResult> GetPhoto(int openingId, int itemId)
    {
        var item = await _db.StoreOpeningItems.FirstOrDefaultAsync(i => i.Id == itemId && i.StoreOpeningId == openingId);
        if (item == null || string.IsNullOrEmpty(item.PhotoUrl)) return NotFound();
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), item.PhotoUrl.TrimStart('/'));
        if (!System.IO.File.Exists(fullPath)) return NotFound();
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var mime = ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
        return PhysicalFile(fullPath, mime);
    }

    [HttpDelete("{openingId:int}/items/{itemId:int}/photo")]
    public async Task<IActionResult> DeletePhoto(int openingId, int itemId)
    {
        var item = await _db.StoreOpeningItems.FirstOrDefaultAsync(i => i.Id == itemId && i.StoreOpeningId == openingId);
        if (item == null) return NotFound();
        if (!string.IsNullOrEmpty(item.PhotoUrl))
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), item.PhotoUrl.TrimStart('/'));
            try { if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath); } catch { }
            item.PhotoUrl = null;
            await _db.SaveChangesAsync();
        }
        return NoContent();
    }

    // ============================================================
    // ACTIVITY LOG
    // ============================================================
    [HttpGet("{openingId:int}/activity")]
    public async Task<ActionResult<List<ActivityDto>>> GetActivity(int openingId)
    {
        var activities = await _db.StoreOpeningActivities
            .Where(a => a.StoreOpeningId == openingId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(200)
            .Select(a => new ActivityDto(a.Id, a.Username, a.Action, a.Details, a.CreatedAt))
            .ToListAsync();
        return Ok(activities);
    }

    // ============================================================
    // EXCEL EXPORT
    // ============================================================
    [HttpGet("{openingId:int}/export.xlsx")]
    public async Task<IActionResult> ExportExcel(int openingId)
    {
        var opening = await _db.StoreOpenings.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == openingId);
        if (opening == null) return NotFound();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Acilis Raporu");

        // Header
        ws.Cell(1, 1).Value = "Mağaza Açılış Raporu";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Range(1, 1, 1, 7).Merge();

        ws.Cell(3, 1).Value = "Mağaza Kodu:"; ws.Cell(3, 2).Value = opening.StoreCode;
        ws.Cell(4, 1).Value = "Mağaza Adı:"; ws.Cell(4, 2).Value = opening.StoreName;
        ws.Cell(5, 1).Value = "Şehir:"; ws.Cell(5, 2).Value = opening.City;
        ws.Cell(6, 1).Value = "Adres:"; ws.Cell(6, 2).Value = opening.Address;
        ws.Cell(7, 1).Value = "Hedef Tarih:"; ws.Cell(7, 2).Value = opening.TargetOpeningDate.ToString("dd.MM.yyyy");
        ws.Cell(8, 1).Value = "Açılış Tarihi:"; ws.Cell(8, 2).Value = opening.ActualOpeningDate?.ToString("dd.MM.yyyy") ?? "";
        ws.Cell(9, 1).Value = "Durum:"; ws.Cell(9, 2).Value = opening.Status;
        ws.Range(3, 1, 9, 1).Style.Font.Bold = true;

        // Items table
        var row = 11;
        ws.Cell(row, 1).Value = "Kategori";
        ws.Cell(row, 2).Value = "Üst Kalem";
        ws.Cell(row, 3).Value = "Kalem";
        ws.Cell(row, 4).Value = "Sorumlu";
        ws.Cell(row, 5).Value = "Seri No";
        ws.Cell(row, 6).Value = "Asset No";
        ws.Cell(row, 7).Value = "Durum";
        ws.Cell(row, 8).Value = "Tamamlayan";
        ws.Cell(row, 9).Value = "Tamamlanma";
        ws.Cell(row, 10).Value = "Not";
        ws.Range(row, 1, row, 10).Style.Font.Bold = true;
        ws.Range(row, 1, row, 10).Style.Fill.BackgroundColor = XLColor.LightGray;
        row++;

        foreach (var item in opening.Items.OrderBy(i => i.CategoryName).ThenBy(i => i.SortOrder))
        {
            ws.Cell(row, 1).Value = item.CategoryName;
            ws.Cell(row, 2).Value = item.ParentName;
            ws.Cell(row, 3).Value = item.ItemName;
            ws.Cell(row, 4).Value = item.AssignedRole;
            ws.Cell(row, 5).Value = item.SerialNumber;
            ws.Cell(row, 6).Value = item.AssetNumber;
            ws.Cell(row, 7).Value = item.Status;
            ws.Cell(row, 8).Value = item.CompletedBy;
            ws.Cell(row, 9).Value = item.CompletedAt?.ToString("dd.MM.yyyy HH:mm");
            ws.Cell(row, 10).Value = item.Notes;
            row++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var fileName = $"acilis-{opening.StoreCode}-{opening.StoreName}.xlsx";
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    // ============================================================
    private static Dictionary<string, string> ParseRoles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch { return new(); }
    }
}
