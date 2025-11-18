using Microsoft.AspNetCore.Mvc;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly string _dataDir;

    public FilesController(IConfiguration configuration)
    {
        _dataDir = configuration["MudoSoft:DataDirectory"] ?? "C:\\MudoSoft\\data";
    }

    // POST /api/files/push/{deviceId}
    [HttpPost("push/{deviceId}")]
    public async Task<IActionResult> Push(string deviceId, IFormFile file, [FromForm] string remotePath)
    {
        // Şimdilik sadece sunucuda temp klasöre kaydediyoruz.
        var uploads = Path.Combine(_dataDir, "uploads");
        Directory.CreateDirectory(uploads);

        var filePath = Path.Combine(uploads, $"{deviceId}_{file.FileName}");
        await using var stream = System.IO.File.Create(filePath);
        await file.CopyToAsync(stream);

        return Ok(new { message = "File received (stub)", path = filePath, remotePath });
    }
}
