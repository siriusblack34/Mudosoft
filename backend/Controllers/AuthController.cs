using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IConfiguration configuration, ILogger<AuthController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Kullanıcı girişi - JWT token döndürür
    /// </summary>
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Username and password are required" });
        }

        // 🔒 SECURITY: Environment variables take priority over appsettings placeholders
        var validUsername = Environment.GetEnvironmentVariable("ADMIN_USERNAME")
            ?? _configuration["Jwt:AdminUsername"]
            ?? throw new InvalidOperationException("ADMIN_USERNAME is not configured");
        var validPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
            ?? _configuration["Jwt:AdminPassword"]
            ?? throw new InvalidOperationException("ADMIN_PASSWORD is not configured");

        // Skip placeholder values from appsettings (e.g. "${ADMIN_USERNAME}")
        if (validUsername.StartsWith("${")) validUsername = Environment.GetEnvironmentVariable("ADMIN_USERNAME")
            ?? throw new InvalidOperationException("ADMIN_USERNAME env var is not set");
        if (validPassword.StartsWith("${")) validPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
            ?? throw new InvalidOperationException("ADMIN_PASSWORD env var is not set");

        if (request.Username != validUsername || request.Password != validPassword)
        {
            _logger.LogWarning("Failed login attempt for user: {Username}", request.Username);
            return Unauthorized(new { error = "Invalid credentials" });
        }

        var token = GenerateJwtToken(request.Username);
        
        _logger.LogInformation("Successful login for user: {Username}", request.Username);
        
        return Ok(new LoginResponse
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            Username = request.Username
        });
    }

    /// <summary>
    /// Agent'lar için API key doğrulaması
    /// </summary>
    [HttpPost("agent-auth")]
    public IActionResult AgentAuth([FromBody] AgentAuthRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequest(new { error = "DeviceId and ApiKey are required" });
        }

        // 🔒 SECURITY: Environment variables take priority over appsettings placeholders
        var validApiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY")
            ?? _configuration["Jwt:AgentApiKey"]
            ?? throw new InvalidOperationException("AGENT_API_KEY is not configured");
        if (validApiKey.StartsWith("${")) validApiKey = Environment.GetEnvironmentVariable("AGENT_API_KEY")
            ?? throw new InvalidOperationException("AGENT_API_KEY env var is not set");

        if (request.ApiKey != validApiKey)
        {
            _logger.LogWarning("Failed agent auth attempt for device: {DeviceId}", request.DeviceId);
            return Unauthorized(new { error = "Invalid API key" });
        }

        var token = GenerateAgentToken(request.DeviceId);
        
        return Ok(new LoginResponse
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(30), // Agent token'lar daha uzun süreli
            Username = request.DeviceId
        });
    }

    /// <summary>
    /// Token yenileme
    /// </summary>
    [HttpPost("refresh")]
    public IActionResult RefreshToken()
    {
        // Mevcut token'dan kullanıcı bilgisini al
        var identity = HttpContext.User.Identity as ClaimsIdentity;
        var username = identity?.FindFirst(ClaimTypes.Name)?.Value;

        if (string.IsNullOrEmpty(username))
        {
            return Unauthorized(new { error = "Invalid token" });
        }

        var newToken = GenerateJwtToken(username);
        
        return Ok(new LoginResponse
        {
            Token = newToken,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            Username = username
        });
    }

    private string GenerateJwtToken(string username)
    {
        var key = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT_SECRET_KEY is not configured");
        if (key.StartsWith("${")) key = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? throw new InvalidOperationException("JWT_SECRET_KEY env var is not set");
        var issuer = _configuration["Jwt:Issuer"] ?? "MudoSoft";
        var audience = _configuration["Jwt:Audience"] ?? "MudoSoftUsers";

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
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
        var issuer = _configuration["Jwt:Issuer"] ?? "MudoSoft";
        var audience = _configuration["Jwt:Audience"] ?? "MudoSoftAgents";

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
            issuer: issuer,
            audience: audience,
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

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Username { get; set; } = string.Empty;
}
