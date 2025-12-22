using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Authorize] // 🔒 Authentication required
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly string _dataDir;
    private readonly ILogger<FilesController> _logger;

    public FilesController(IConfiguration configuration, ILogger<FilesController> logger)
    {
        _dataDir = configuration["MudoSoft:DataDirectory"] ?? "C:\\MudoSoft\\data";
        _logger = logger;
    }

    // POST /api/files/push/{deviceId}
    [HttpPost("push/{deviceId}")]
    public async Task<IActionResult> Push(string deviceId, IFormFile file, [FromForm] string remotePath)
    {
        // 🔒 Input validation
        if (string.IsNullOrWhiteSpace(deviceId) || !IsValidDeviceId(deviceId))
        {
            return BadRequest(new { error = "Invalid device ID" });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided" });
        }

        // 🔒 Filename sanitization - prevent path traversal
        var safeFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Contains(".."))
        {
            return BadRequest(new { error = "Invalid filename" });
        }

        var uploads = Path.Combine(_dataDir, "uploads");
        Directory.CreateDirectory(uploads);

        // 🔒 Construct safe path
        var filePath = Path.Combine(uploads, $"{deviceId}_{safeFileName}");
        
        // 🔒 Verify path is within allowed directory
        var fullPath = Path.GetFullPath(filePath);
        if (!fullPath.StartsWith(Path.GetFullPath(uploads), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal attempt detected: {FileName}", file.FileName);
            return BadRequest(new { error = "Invalid file path" });
        }

        await using var stream = System.IO.File.Create(filePath);
        await file.CopyToAsync(stream);

        _logger.LogInformation("File uploaded: {FilePath}", filePath);
        return Ok(new { message = "File received", path = filePath, remotePath });
    }

    private static bool IsValidDeviceId(string deviceId)
    {
        // DeviceId sadece alfanümerik ve tire içerebilir
        return System.Text.RegularExpressions.Regex.IsMatch(deviceId, @"^[a-zA-Z0-9\-_]+$");
    }
}
