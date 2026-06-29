using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly ILogger<UsersController> _logger;

    public UsersController(OrchestraDbContext db, ILogger<UsersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _db.Users
            .Include(u => u.MenuProfile)
            .OrderBy(u => u.Username)
            .ToListAsync();

        return Ok(users.Select(u => new
        {
            u.Id, u.Username, u.FullName, u.Role, u.IsActive,
            u.CreatedAt, u.LastLoginAt, u.Email,
            u.MenuProfileId,
            MenuProfileName = u.MenuProfile?.Name,
            MenuGrants = ParseJson(u.MenuGrantsJson),
            MenuDenials = ParseJson(u.MenuDenialsJson)
        }));
    }

    private static string[] ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static string? SerializeOverride(string[]? paths)
    {
        if (paths == null) return null;
        var clean = paths.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToArray();
        return clean.Length == 0 ? null : System.Text.Json.JsonSerializer.Serialize(clean);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Kullanıcı adı ve şifre gerekli" });

        if (await _db.Users.AnyAsync(u => u.Username == req.Username.Trim().ToLower()))
            return BadRequest(new { error = "Bu kullanıcı adı zaten mevcut" });

        if (req.MenuProfileId.HasValue && !await _db.MenuProfiles.AnyAsync(p => p.Id == req.MenuProfileId.Value))
            return BadRequest(new { error = "Geçersiz menü profili" });

        var user = new User
        {
            Username = req.Username.Trim().ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = req.Role is "Admin" or "Teknisyen" ? req.Role : "Teknisyen",
            FullName = req.FullName?.Trim() ?? req.Username,
            Email = req.Email?.Trim(),
            MenuProfileId = req.MenuProfileId,
            MenuGrantsJson = SerializeOverride(req.MenuGrants),
            MenuDenialsJson = SerializeOverride(req.MenuDenials),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User created: {Username} ({Role})", user.Username, user.Role);
        return Ok(new { user.Id, user.Username, user.FullName, user.Role, user.Email, user.MenuProfileId });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { error = "Kullanıcı bulunamadı" });

        if (!string.IsNullOrWhiteSpace(req.FullName)) user.FullName = req.FullName.Trim();
        if (req.Role is "Admin" or "Teknisyen") user.Role = req.Role;
        if (req.IsActive.HasValue) user.IsActive = req.IsActive.Value;
        if (req.Email != null) user.Email = req.Email.Trim() == "" ? null : req.Email.Trim();

        // Menü profili: -1 = "profili kaldır (varsayılana dön)", null = "değiştirme", >0 = "ata".
        if (req.MenuProfileId.HasValue)
        {
            if (req.MenuProfileId.Value <= 0)
            {
                user.MenuProfileId = null;
            }
            else
            {
                if (!await _db.MenuProfiles.AnyAsync(p => p.Id == req.MenuProfileId.Value))
                    return BadRequest(new { error = "Geçersiz menü profili" });
                user.MenuProfileId = req.MenuProfileId.Value;
            }
        }
        if (req.MenuGrants != null) user.MenuGrantsJson = SerializeOverride(req.MenuGrants);
        if (req.MenuDenials != null) user.MenuDenialsJson = SerializeOverride(req.MenuDenials);

        await _db.SaveChangesAsync();
        _logger.LogInformation("User updated: {Username}", user.Username);
        return Ok(new { user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.Email, user.MenuProfileId });
    }

    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 4)
            return BadRequest(new { error = "Şifre en az 4 karakter olmalı" });

        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { error = "Kullanıcı bulunamadı" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Password reset for user: {Username}", user.Username);
        return Ok(new { success = true, message = $"{user.Username} şifresi sıfırlandı" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { error = "Kullanıcı bulunamadı" });

        // Son admin silinemesin
        if (user.Role == "Admin")
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == "Admin" && u.IsActive);
            if (adminCount <= 1)
                return BadRequest(new { error = "Son admin kullanıcı silinemez" });
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User deleted: {Username}", user.Username);
        return Ok(new { success = true });
    }

    [HttpGet("login-history")]
    public async Task<IActionResult> GetLoginHistory([FromQuery] int limit = 50)
    {
        var history = await _db.LoginHistories
            .OrderByDescending(l => l.LoginAt)
            .Take(limit)
            .Select(l => new { l.Id, l.Username, l.LoginAt, l.IpAddress, l.Success })
            .ToListAsync();
        return Ok(history);
    }
}

public class CreateUserRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "Teknisyen";
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public int? MenuProfileId { get; set; }
    public string[]? MenuGrants { get; set; }
    public string[]? MenuDenials { get; set; }
}

public class UpdateUserRequest
{
    public string? FullName { get; set; }
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
    public string? Email { get; set; }
    /// <summary>>0 ata, -1 kaldır (varsayılana dön), null değiştirme.</summary>
    public int? MenuProfileId { get; set; }
    public string[]? MenuGrants { get; set; }
    public string[]? MenuDenials { get; set; }
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = "";
}
