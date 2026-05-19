using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/store-opening-templates")]
public class StoreOpeningTemplatesController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    public StoreOpeningTemplatesController(OrchestraDbContext db) { _db = db; }

    public record TemplateListDto(int Id, string Name, string? Description, bool IsDefault, int ItemCount, DateTime CreatedAt);

    public record TemplateItemInputDto(
        string CategoryName,
        string? AssignedRole,
        string ItemName,
        string? ParentName,
        bool HasSerialNumber,
        bool HasAssetNumber,
        int SortOrder
    );

    public record TemplateInputDto(
        string Name,
        string? Description,
        bool IsDefault,
        List<TemplateItemInputDto> Items
    );

    public record TemplateDetailDto(
        int Id,
        string Name,
        string? Description,
        bool IsDefault,
        DateTime CreatedAt,
        List<StoreOpeningTemplateItem> Items
    );

    [HttpGet]
    public async Task<ActionResult<List<TemplateListDto>>> List()
    {
        var rows = await _db.StoreOpeningTemplates
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .Select(t => new TemplateListDto(t.Id, t.Name, t.Description, t.IsDefault, t.Items.Count, t.CreatedAt))
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TemplateDetailDto>> Get(int id)
    {
        var t = await _db.StoreOpeningTemplates.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        var items = t.Items.OrderBy(i => i.SortOrder).ToList();
        return Ok(new TemplateDetailDto(t.Id, t.Name, t.Description, t.IsDefault, t.CreatedAt, items));
    }

    [HttpPost]
    public async Task<ActionResult<TemplateDetailDto>> Create([FromBody] TemplateInputDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { error = "Name zorunlu" });
        var username = User.FindFirstValue(ClaimTypes.Name) ?? "system";

        if (dto.IsDefault)
            await _db.StoreOpeningTemplates.Where(t => t.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false));

        var t = new StoreOpeningTemplate
        {
            Name = dto.Name.Trim(),
            Description = dto.Description,
            IsDefault = dto.IsDefault,
            CreatedBy = username,
            Items = dto.Items.Select(i => new StoreOpeningTemplateItem
            {
                CategoryName = i.CategoryName.Trim(),
                AssignedRole = i.AssignedRole,
                ItemName = i.ItemName.Trim(),
                ParentName = i.ParentName?.Trim(),
                HasSerialNumber = i.HasSerialNumber,
                HasAssetNumber = i.HasAssetNumber,
                SortOrder = i.SortOrder
            }).ToList()
        };
        _db.StoreOpeningTemplates.Add(t);
        await _db.SaveChangesAsync();
        return await Get(t.Id);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TemplateInputDto dto)
    {
        var t = await _db.StoreOpeningTemplates.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();

        if (dto.IsDefault && !t.IsDefault)
            await _db.StoreOpeningTemplates.Where(x => x.IsDefault && x.Id != id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false));

        t.Name = dto.Name.Trim();
        t.Description = dto.Description;
        t.IsDefault = dto.IsDefault;

        _db.StoreOpeningTemplateItems.RemoveRange(t.Items);
        foreach (var i in dto.Items)
        {
            t.Items.Add(new StoreOpeningTemplateItem
            {
                CategoryName = i.CategoryName.Trim(),
                AssignedRole = i.AssignedRole,
                ItemName = i.ItemName.Trim(),
                ParentName = i.ParentName?.Trim(),
                HasSerialNumber = i.HasSerialNumber,
                HasAssetNumber = i.HasAssetNumber,
                SortOrder = i.SortOrder
            });
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await _db.StoreOpeningTemplates.FindAsync(id);
        if (t == null) return NotFound();
        _db.StoreOpeningTemplates.Remove(t);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
