using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Services;
using System.IO.Compression;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/updates")]
public class UpdateController : ControllerBase
{
    private readonly ILogger<UpdateController> _logger;
    private readonly string _updatesPath;
    private readonly string _latestVersionFile;

    public UpdateController(ILogger<UpdateController> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _updatesPath = Path.Combine(env.ContentRootPath, "updates");
        _latestVersionFile = Path.Combine(_updatesPath, "latest.json");
        
        // Ensure updates directory exists
        if (!Directory.Exists(_updatesPath))
        {
            Directory.CreateDirectory(_updatesPath);
        }
    }

    /// <summary>
    /// Upload new agent package
    /// POST /api/updates/upload
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100_000_000)] // 100MB limit
    public async Task<IActionResult> UploadAgent([FromForm] IFormFile file, [FromForm] string version)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        if (string.IsNullOrWhiteSpace(version))
            return BadRequest("Version is required");

        try
        {
            // Save the zip file
            var fileName = $"MudoSoft.Agent_{version}.zip";
            var filePath = Path.Combine(_updatesPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Update latest.json
            var latestInfo = new
            {
                version = version,
                fileName = fileName,
                uploadedAt = DateTime.UtcNow.ToString("o"),
                sizeBytes = file.Length
            };

            await System.IO.File.WriteAllTextAsync(
                _latestVersionFile,
                System.Text.Json.JsonSerializer.Serialize(latestInfo)
            );

            _logger.LogInformation("Agent version {Version} uploaded successfully", version);

            return Ok(new { message = $"Agent {version} uploaded", fileName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload agent");
            return StatusCode(500, "Failed to upload agent");
        }
    }

    /// <summary>
    /// Get latest version info
    /// GET /api/updates/latest
    /// </summary>
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatestVersion()
    {
        if (!System.IO.File.Exists(_latestVersionFile))
        {
            return Ok(new { version = "none", message = "No updates available" });
        }

        var json = await System.IO.File.ReadAllTextAsync(_latestVersionFile);
        return Content(json, "application/json");
    }

    /// <summary>
    /// Download latest agent package
    /// GET /api/updates/download
    /// </summary>
    [HttpGet("download")]
    public async Task<IActionResult> DownloadLatest()
    {
        if (!System.IO.File.Exists(_latestVersionFile))
        {
            return NotFound("No updates available");
        }

        var json = await System.IO.File.ReadAllTextAsync(_latestVersionFile);
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var info = System.Text.Json.JsonSerializer.Deserialize<LatestVersionInfo>(json, options);
        
        if (info == null || string.IsNullOrEmpty(info.FileName))
        {
            return NotFound($"Invalid version info. FileName is null. JSON: {json}");
        }

        var filePath = Path.Combine(_updatesPath, info.FileName);
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("Agent package not found");
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "application/zip", info.FileName);
    }

    /// <summary>
    /// Download specific version
    /// GET /api/updates/download/{version}
    /// </summary>
    [HttpGet("download/{version}")]
    public async Task<IActionResult> DownloadVersion(string version)
    {
        var fileName = $"MudoSoft.Agent_{version}.zip";
        var filePath = Path.Combine(_updatesPath, fileName);
        
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound($"Version {version} not found");
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "application/zip", fileName);
    }

    /// <summary>
    /// Trigger update on a specific device
    /// POST /api/updates/trigger
    /// </summary>
    [HttpPost("trigger")]
    public IActionResult TriggerUpdate(
        [FromQuery] string deviceId, 
        [FromQuery] string? backendUrl,
        [FromServices] MudoSoft.Backend.Data.CommandQueue queue)
    {
        if (string.IsNullOrEmpty(deviceId))
            return BadRequest("deviceId required");

        // Use provided backendUrl or fallback to Request.Host
        var url = !string.IsNullOrEmpty(backendUrl) 
            ? backendUrl 
            : $"{Request.Scheme}://{Request.Host}";

        var commandId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            backendUrl = url
        });

        queue.Enqueue(new Mudosoft.Shared.Dtos.CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = Mudosoft.Shared.Enums.CommandType.UpdateAgent,
            Payload = payload,
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("Update command queued for device {DeviceId}", deviceId);

        return Ok(new { commandId, message = "Update command queued" });
    }

    /// <summary>
    /// Trigger update on all online devices
    /// POST /api/updates/trigger-all
    /// </summary>
    [HttpPost("trigger-all")]
    public async Task<IActionResult> TriggerUpdateAll(
        [FromQuery] string? backendUrl,
        [FromServices] MudoSoft.Backend.Data.CommandQueue queue, 
        [FromServices] MudoSoft.Backend.Data.MudoSoftDbContext dbContext)
    {
        // Use provided backendUrl or fallback to Request.Host
        var url = !string.IsNullOrEmpty(backendUrl) 
            ? backendUrl 
            : $"{Request.Scheme}://{Request.Host}";
        var payload = System.Text.Json.JsonSerializer.Serialize(new { backendUrl = url });

        var onlineDevices = await dbContext.Devices
            .Where(d => d.Online && !string.IsNullOrEmpty(d.AgentVersion))
            .Select(d => d.Id)
            .ToListAsync();

        foreach (var deviceId in onlineDevices)
        {
            queue.Enqueue(new Mudosoft.Shared.Dtos.CommandDto
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                Type = Mudosoft.Shared.Enums.CommandType.UpdateAgent,
                Payload = payload,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        _logger.LogInformation("Update command queued for {Count} devices", onlineDevices.Count);

        return Ok(new { count = onlineDevices.Count, message = $"Update queued for {onlineDevices.Count} devices" });
    }

    private class LatestVersionInfo
    {
        public string? Version { get; set; }
        public string? FileName { get; set; }
        public string? UploadedAt { get; set; }
        public long SizeBytes { get; set; }
    }
}
