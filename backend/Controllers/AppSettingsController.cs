using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/app-settings")]
[Authorize]
public class AppSettingsController : ControllerBase
{
    private readonly MudoSoftDbContext _db;
    private readonly IEmailService _emailService;

    public AppSettingsController(MudoSoftDbContext db, IEmailService emailService)
    {
        _db = db;
        _emailService = emailService;
    }

    /// <summary>
    /// Teknisyenlerin goremeyecegi menu path'lerini dondurur.
    /// </summary>
    [HttpGet("hidden-menus")]
    public async Task<IActionResult> GetHiddenMenus()
    {
        var setting = await _db.AppSettings.FindAsync("hiddenMenusForTechnician");
        if (setting == null)
            return Ok(Array.Empty<string>());

        return Ok(System.Text.Json.JsonSerializer.Deserialize<string[]>(setting.Value) ?? Array.Empty<string>());
    }

    /// <summary>
    /// Teknisyenlerin goremeyecegi menu path'lerini kaydeder. Sadece Admin.
    /// </summary>
    [HttpPut("hidden-menus")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetHiddenMenus([FromBody] string[] hiddenPaths)
    {
        var setting = await _db.AppSettings.FindAsync("hiddenMenusForTechnician");
        var json = System.Text.Json.JsonSerializer.Serialize(hiddenPaths);

        if (setting == null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = "hiddenMenusForTechnician",
                Value = json,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(hiddenPaths);
    }

    // ════════════════════════════════════════
    // SMTP AYARLARI
    // ════════════════════════════════════════

    [HttpGet("smtp")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetSmtpConfig()
    {
        var keys = new[] { "smtp:host", "smtp:port", "smtp:username", "smtp:useSsl", "smtp:fromAddress", "smtp:fromName" };
        var settings = await _db.AppSettings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        return Ok(new
        {
            host = settings.GetValueOrDefault("smtp:host", ""),
            port = int.TryParse(settings.GetValueOrDefault("smtp:port", "587"), out var p) ? p : 587,
            username = settings.GetValueOrDefault("smtp:username", ""),
            hasPassword = await _db.AppSettings.AnyAsync(s => s.Key == "smtp:password" && s.Value != ""),
            useSsl = settings.GetValueOrDefault("smtp:useSsl", "false") == "true",
            fromAddress = settings.GetValueOrDefault("smtp:fromAddress", ""),
            fromName = settings.GetValueOrDefault("smtp:fromName", "MudoSoft RMM")
        });
    }

    [HttpPut("smtp")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetSmtpConfig([FromBody] SmtpConfigRequest req)
    {
        // Validation
        if (!string.IsNullOrEmpty(req.Host) && req.Host.Length > 253)
            return BadRequest(new { error = "SMTP host çok uzun (max 253 karakter)" });
        if (req.Port < 1 || req.Port > 65535)
            return BadRequest(new { error = "Port 1-65535 aralığında olmalı" });
        if (!string.IsNullOrEmpty(req.FromAddress) && !req.FromAddress.Contains('@'))
            return BadRequest(new { error = "Geçersiz gönderen e-posta adresi" });

        var emailService = (EmailService)_emailService;

        var pairs = new Dictionary<string, string>
        {
            ["smtp:host"] = req.Host ?? "",
            ["smtp:port"] = req.Port.ToString(),
            ["smtp:username"] = req.Username ?? "",
            ["smtp:useSsl"] = req.UseSsl ? "true" : "false",
            ["smtp:fromAddress"] = req.FromAddress ?? "",
            ["smtp:fromName"] = req.FromName ?? "MudoSoft RMM"
        };

        // Sifre sadece doluysa guncellenir (bos gonderilirse mevcut korunur)
        if (!string.IsNullOrEmpty(req.Password))
        {
            pairs["smtp:password"] = emailService.EncryptPassword(req.Password);
        }

        foreach (var (key, value) in pairs)
        {
            var setting = await _db.AppSettings.FindAsync(key);
            if (setting == null)
            {
                _db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
            }
            else
            {
                setting.Value = value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("smtp/test")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TestSmtpConnection()
    {
        var (success, error) = await _emailService.TestConnectionAsync();
        return Ok(new { success, message = error });
    }

    // ════════════════════════════════════════
    // ALARM AYARLARI
    // ════════════════════════════════════════

    [HttpPost("smtp/send-test-email")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SendTestEmail()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            return Unauthorized(new { success = false, message = "Kullanici bilgisi bulunamadi" });

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user == null)
            return NotFound(new { success = false, message = "Kullanici kaydi bulunamadi" });

        if (string.IsNullOrWhiteSpace(user.Email))
            return BadRequest(new { success = false, message = "Test e-postasi icin once kullaniciniza e-posta adresi tanimlayin" });

        var (success, message) = await _emailService.SendTestEmailAsync(user.Email, username);
        return Ok(new { success, message });
    }

    [HttpGet("alarm")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAlarmConfig()
    {
        var setting = await _db.AppSettings.FindAsync("alarm:config");
        if (setting == null)
            return Ok(new AlarmConfigDto { EmailAlertsEnabled = false, AlertRecipientRoles = new[] { "Admin" }, CooldownMinutes = 30 });

        var config = System.Text.Json.JsonSerializer.Deserialize<AlarmConfigDto>(setting.Value);
        return Ok(config ?? new AlarmConfigDto());
    }

    [HttpPut("alarm")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetAlarmConfig([FromBody] AlarmConfigDto req)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(req);
        var setting = await _db.AppSettings.FindAsync("alarm:config");

        if (setting == null)
        {
            _db.AppSettings.Add(new AppSetting { Key = "alarm:config", Value = json, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(req);
    }
}

public class SmtpConfigRequest
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseSsl { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
}

public class AlarmConfigDto
{
    public bool EmailAlertsEnabled { get; set; }
    public string[] AlertRecipientRoles { get; set; } = new[] { "Admin" };
    public int CooldownMinutes { get; set; } = 30;
}
