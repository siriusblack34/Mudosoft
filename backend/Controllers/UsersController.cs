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
            .OrderBy(u => u.Username)
            .Select(u => new
            {
                u.Id, u.Username, u.FullName, u.Role, u.IsActive,
                u.CreatedAt, u.LastLoginAt, u.Email
            })
            .ToListAsync();
        return Ok(users);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Kullanıcı adı ve şifre gerekli" });

        if (await _db.Users.AnyAsync(u => u.Username == req.Username.Trim().ToLower()))
            return BadRequest(new { error = "Bu kullanıcı adı zaten mevcut" });

        var user = new User
        {
            Username = req.Username.Trim().ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = req.Role is "Admin" or "Teknisyen" ? req.Role : "Teknisyen",
            FullName = req.FullName?.Trim() ?? req.Username,
            Email = req.Email?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User created: {Username} ({Role})", user.Username, user.Role);
        return Ok(new { user.Id, user.Username, user.FullName, user.Role, user.Email });
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

        await _db.SaveChangesAsync();
        _logger.LogInformation("User updated: {Username}", user.Username);
        return Ok(new { user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.Email });
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
}

public class UpdateUserRequest
{
    public string? FullName { get; set; }
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
    public string? Email { get; set; }
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = "";
}
