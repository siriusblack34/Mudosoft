using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly MudoSoftDbContext _db;

    public AuthController(IConfiguration configuration, ILogger<AuthController> logger, MudoSoftDbContext db)
    {
        _configuration = configuration;
        _logger = logger;
        _db = db;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Username and password are required" });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var username = request.Username.Trim().ToLower();

        // DB'den kullanıcı ara
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user != null && BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            // Başarılı giriş
            user.LastLoginAt = DateTime.UtcNow;
            _db.LoginHistories.Add(new LoginHistory
            {
                UserId = user.Id, Username = user.Username, IpAddress = ip, Success = true
            });
            await _db.SaveChangesAsync();

            var token = GenerateJwtToken(user.Username, user.Role, user.Id);
            _logger.LogInformation("Login OK: {Username} ({Role}) from {IP}", user.Username, user.Role, ip);

            return Ok(new LoginResponse
            {
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddHours(8),
                Username = user.Username,
                Role = user.Role,
                FullName = user.FullName
            });
        }

        // Env-var fallback (ilk kurulumda DB boşken)
        var envUser = Environment.GetEnvironmentVariable("ADMIN_USERNAME")
            ?? _configuration["Jwt:AdminUsername"];
        var envPass = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
            ?? _configuration["Jwt:AdminPassword"];

        if (!string.IsNullOrEmpty(envUser) && !envUser.StartsWith("${") &&
            !string.IsNullOrEmpty(envPass) && !envPass.StartsWith("${") &&
            request.Username == envUser && request.Password == envPass)
        {
            _db.LoginHistories.Add(new LoginHistory
            {
                Username = request.Username, IpAddress = ip, Success = true
            });
            await _db.SaveChangesAsync();

            var token = GenerateJwtToken(request.Username, "Admin", null);
            _logger.LogInformation("Login OK (env fallback): {Username} from {IP}", request.Username, ip);

            return Ok(new LoginResponse
            {
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddHours(8),
                Username = request.Username,
                Role = "Admin",
                FullName = "Administrator"
            });
        }

        // Başarısız giriş
        _db.LoginHistories.Add(new LoginHistory
        {
            Username = request.Username, IpAddress = ip, Success = false
        });
        await _db.SaveChangesAsync();

        _logger.LogWarning("Failed login: {Username} from {IP}", request.Username, ip);
        return Unauthorized(new { error = "Geçersiz kullanıcı adı veya şifre" });
    }

    [HttpPost("agent-auth")]
    public IActionResult AgentAuth([FromBody] AgentAuthRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.ApiKey))
            return BadRequest(new { error = "DeviceId and ApiKey are required" });

        var validApiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY")
            ?? _configuration["Jwt:AgentApiKey"]
            ?? throw new InvalidOperationException("AGENT_API_KEY is not configured");
        if (validApiKey.StartsWith("${")) validApiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY")
            ?? throw new InvalidOperationException("AGENT_API_KEY env var is not set");

        if (request.ApiKey != validApiKey)
        {
            _logger.LogWarning("Failed agent auth: {DeviceId}", request.DeviceId);
            return Unauthorized(new { error = "Invalid API key" });
        }

        var token = GenerateAgentToken(request.DeviceId);
        return Ok(new LoginResponse
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Username = request.DeviceId,
            Role = "Agent",
            FullName = request.DeviceId
        });
    }

    [HttpPost("change-password")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { error = "Mevcut şifre ve yeni şifre gerekli" });
        if (req.NewPassword.Length < 4)
            return BadRequest(new { error = "Yeni şifre en az 4 karakter olmalı" });

        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Unauthorized(new { error = "Oturum bulunamadı" });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
        if (user == null)
            return NotFound(new { error = "Kullanıcı bulunamadı" });

        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { error = "Mevcut şifre yanlış" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Password changed by user: {Username}", username);
        return Ok(new { success = true, message = "Şifreniz başarıyla değiştirildi" });
    }

    [HttpPost("refresh")]
    public IActionResult RefreshToken()
    {
        var identity = HttpContext.User.Identity as ClaimsIdentity;
        var username = identity?.FindFirst(ClaimTypes.Name)?.Value;
        var role = identity?.FindFirst(ClaimTypes.Role)?.Value ?? "Admin";

        if (string.IsNullOrEmpty(username))
            return Unauthorized(new { error = "Invalid token" });

        var newToken = GenerateJwtToken(username, role, null);
        return Ok(new LoginResponse
        {
            Token = newToken,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            Username = username,
            Role = role,
            FullName = username
        });
    }

    private string GenerateJwtToken(string username, string role, int? userId)
    {
        var key = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT_SECRET_KEY is not configured");
        if (key.StartsWith("${")) key = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? throw new InvalidOperationException("JWT_SECRET_KEY env var is not set");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };
        if (userId.HasValue)
            claims.Add(new Claim("UserId", userId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "MudoSoft",
            audience: _configuration["Jwt:Audience"] ?? "MudoSoftUsers",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateAgentToken(string deviceId)
    {
        var key = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT_SECRET_KEY is not configured");
        if (key.StartsWith("${")) key = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? throw new InvalidOperationException("JWT_SECRET_KEY env var is not set");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, deviceId),
            new Claim(ClaimTypes.Role, "Agent"),
            new Claim("DeviceId", deviceId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "MudoSoft",
            audience: _configuration["Jwt:Audience"] ?? "MudoSoftAgents",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AgentAuthRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}
