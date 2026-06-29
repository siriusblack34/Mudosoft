using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Controllers;

/// <summary>
/// Menü profilleri (yetki grupları) yönetimi. Sadece Admin.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/menu-profiles")]
public class MenuProfilesController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly ILogger<MenuProfilesController> _logger;

    public MenuProfilesController(OrchestraDbContext db, ILogger<MenuProfilesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var profiles = await _db.MenuProfiles
            .OrderByDescending(p => p.IsSystem)
            .ThenBy(p => p.Name)
            .ToListAsync();

        // Profil başına atanmış kullanıcı sayısı (silme/uyarı için faydalı).
        var counts = await _db.Users
            .Where(u => u.MenuProfileId != null)
            .GroupBy(u => u.MenuProfileId!.Value)
            .Select(g => new { ProfileId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProfileId, x => x.Count);

        return Ok(profiles.Select(p => ToDto(p, counts.GetValueOrDefault(p.Id, 0))));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MenuProfileRequest req)
    {
        var name = req.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Profil adı gerekli" });
        if (await _db.MenuProfiles.AnyAsync(p => p.Name == name))
            return BadRequest(new { error = "Bu isimde bir profil zaten var" });

        var profile = new MenuProfile
        {
            Name = name,
            Description = req.Description?.Trim(),
            AllowAllByDefault = req.AllowAllByDefault,
            AllowedMenusJson = Serialize(req.AllowedMenus),
            HiddenMenusJson = Serialize(req.HiddenMenus),
            IsSystem = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.MenuProfiles.Add(profile);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Menu profile created: {Name}", profile.Name);
        return Ok(ToDto(profile, 0));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] MenuProfileRequest req)
    {
        var profile = await _db.MenuProfiles.FindAsync(id);
        if (profile == null) return NotFound(new { error = "Profil bulunamadı" });

        // Sistem profillerinin adı değiştirilemez (Teknisyen/Superuser), menüleri düzenlenebilir.
        if (!profile.IsSystem && !string.IsNullOrWhiteSpace(req.Name))
        {
            var name = req.Name.Trim();
            if (name != profile.Name && await _db.MenuProfiles.AnyAsync(p => p.Name == name && p.Id != id))
                return BadRequest(new { error = "Bu isimde bir profil zaten var" });
            profile.Name = name;
        }

        if (req.Description != null) profile.Description = req.Description.Trim();
        profile.AllowAllByDefault = req.AllowAllByDefault;
        profile.AllowedMenusJson = Serialize(req.AllowedMenus);
        profile.HiddenMenusJson = Serialize(req.HiddenMenus);
        profile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Menu profile updated: {Name}", profile.Name);

        var count = await _db.Users.CountAsync(u => u.MenuProfileId == id);
        return Ok(ToDto(profile, count));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var profile = await _db.MenuProfiles.FindAsync(id);
        if (profile == null) return NotFound(new { error = "Profil bulunamadı" });
        if (profile.IsSystem)
            return BadRequest(new { error = "Sistem profili silinemez" });

        // Bu profile bağlı kullanıcıların ataması FK SetNull ile null'a düşer → Teknisyen'e geri döner.
        _db.MenuProfiles.Remove(profile);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Menu profile deleted: {Name}", profile.Name);
        return Ok(new { success = true });
    }

    private static object ToDto(MenuProfile p, int userCount) => new
    {
        p.Id,
        p.Name,
        p.Description,
        p.IsSystem,
        p.AllowAllByDefault,
        AllowedMenus = Parse(p.AllowedMenusJson),
        HiddenMenus = Parse(p.HiddenMenusJson),
        UserCount = userCount,
        p.UpdatedAt
    };

    private static string Serialize(string[]? paths) =>
        JsonSerializer.Serialize((paths ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct()
            .ToArray());

    private static string[] Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }
}

public class MenuProfileRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool AllowAllByDefault { get; set; }
    public string[]? AllowedMenus { get; set; }
    public string[]? HiddenMenus { get; set; }
}
