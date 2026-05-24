using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Orchestra.Shared.Dtos;
using Orchestra.Shared.Enums;
using Orchestra.Backend.Services;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize] // 🔒 Authentication required by default
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _service;
    private readonly CommandQueue _queue;
    private readonly ILogger<AgentController> _logger;
    private readonly OrchestraDbContext _dbContext;

    public AgentController(
        IAgentService service,
        CommandQueue queue,
        ILogger<AgentController> logger,
        OrchestraDbContext dbContext)
    {
        _service = service;
        _queue = queue;
        _logger = logger;
        _dbContext = dbContext;
    }

    // ❤️ Heartbeat (decrypt edilmiş DTO middleware'den gelir)
    // 🔥 Agent'lar RSA/AES şifreleme ile authenticate oluyor
    [AllowAnonymous] // Agent encrypted payload ile iletişim kuruyor
    [HttpPost("report")] 
    public async Task<IActionResult> Heartbeat([FromBody] DeviceHeartbeatDto dto)
    {
        if (dto == null)
            return BadRequest("DTO null");

        // Agent self-report yanlış NIC seçmiş olabilir (multi-NIC laptop'larda VPN/virtual switch karışıklığı).
        // Backend'in TCP connection'da gördüğü gerçek source IP'yi de saklayalım — /rdp/check fallback'i için.
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString();
        await _service.HandleHeartbeatAsync(dto, remoteIp);
        return Ok();
    }


    // 📥 Commands Poll
    [AllowAnonymous] // Agent encrypted payload ile iletişim kuruyor
    [HttpGet("commands")]
    public async Task<ActionResult<List<CommandDto>>> GetCommands([FromQuery] string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest("deviceId required");

        var cmds = await _service.GetCommandsAsync(deviceId);
        return Ok(cmds);
    }

    // 📤 Command Result
    [AllowAnonymous] // Agent encrypted payload ile iletişim kuruyor
    [HttpPost("command-result")]
    public async Task<IActionResult> CommandResult([FromBody] CommandResultDto result)
    {
        await _service.HandleCommandResultAsync(result);
        return Ok();
    }

    // 🚨 Events
    [AllowAnonymous] // Agent encrypted payload ile iletişim kuruyor
    [HttpPost("events")]
    public async Task<IActionResult> Events([FromBody] DeviceEventDto evt)
    {
        await _service.HandleEventAsync(evt);
        return Ok();
    }

    // 🧪 Test command enqueue
    [HttpPost("enqueue-test-command")]
    public IActionResult EnqueueTestCommand(string deviceId)
    {
        _queue.Enqueue(new CommandDto
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            Type = CommandType.Reboot,
            CreatedAtUtc = DateTime.UtcNow
        });

        return Ok("Test command queued.");
    }

    // 🏆 Ön Uca: Son Komut Sonucu
    [HttpGet("command-results/latest")]
    public async Task<ActionResult<CommandResultRecord>> GetLatestCommandResult([FromQuery] string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return BadRequest("Device ID gereklidir.");

        var latestResult = await _dbContext.CommandResultRecords
            .Where(r => r.DeviceId == deviceId)
            .OrderByDescending(r => r.CompletedAtUtc)
            .FirstOrDefaultAsync();

        if (latestResult == null)
            return Ok(new CommandResultRecord { Output = "Henüz komut sonucu kaydedilmedi." });

        return Ok(latestResult);
    }

    #region VNC Management

    /// <summary>
    /// Agent reports VNC installation status and password.
    /// Called by agent after TightVNC is installed.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("vnc-status")]
    public async Task<IActionResult> VncStatus([FromBody] VncStatusReport report)
    {
        if (string.IsNullOrEmpty(report?.DeviceId))
            return BadRequest("deviceId required");

        var device = await _dbContext.Devices.FindAsync(report.DeviceId);
        if (device == null)
            return NotFound("Device not found");

        device.VncInstalled = report.Installed;
        device.VncPassword = report.Password; // Agent sends the generated password
        device.VncPort = report.Port > 0 ? report.Port : 5900;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("[VNC] Device {DeviceId} VNC status updated: installed={Installed}, port={Port}",
            report.DeviceId, report.Installed, report.Port);

        return Ok();
    }

    /// <summary>
    /// Trigger VNC installation on a specific device.
    /// </summary>
    [HttpPost("vnc-install/{deviceId}")]
    public IActionResult TriggerVncInstall(string deviceId)
    {
        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = CommandType.InstallVnc,
            CreatedAtUtc = DateTime.UtcNow
        });

        return Ok(new { commandId, message = "VNC install command queued" });
    }

    /// <summary>
    /// Hedef cihazda Orchestra agent'i, TightVNC'yi, kurulum klasorunu, update klasorlerini
    /// ve helper log'u uzaktan tamamen kaldirir. Admin yetkisi gerekir.
    /// </summary>
    [HttpPost("uninstall/{deviceId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TriggerUninstall(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest(new { error = "deviceId required" });

        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = CommandType.UninstallAgent,
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogWarning("[UNINSTALL] Uninstall komutu kuyruga eklendi: DeviceId={DeviceId} CommandId={CommandId} User={User}",
            deviceId, commandId, User?.Identity?.Name);

        // Cihaz kaydini ve iliskili kayitlari listeden hemen kaldir.
        // Komut in-memory queue'da kaldigi icin agent yine de cekip calistirabilir.
        // Heartbeat handler'da pending UninstallAgent kontrolu sayesinde aradaki heartbeat
        // ile cihaz tekrar olusmaz.
        var device = await _dbContext.Devices.FindAsync(deviceId);
        if (device != null)
        {
            await _dbContext.DeviceMetrics.Where(m => m.DeviceId == deviceId).ExecuteDeleteAsync();
            await _dbContext.CommandResultRecords.Where(r => r.DeviceId == deviceId).ExecuteDeleteAsync();
            await _dbContext.CollectorReports.Where(r => r.DeviceId == deviceId).ExecuteDeleteAsync();
            await _dbContext.VncSessionLogs.Where(r => r.DeviceId == deviceId).ExecuteDeleteAsync();
            _dbContext.Devices.Remove(device);
            await _dbContext.SaveChangesAsync();
            _logger.LogWarning("[UNINSTALL] Cihaz kaydi silindi: DeviceId={DeviceId}", deviceId);
        }

        return Ok(new { commandId, message = "Uninstall command queued. Agent will remove itself, TightVNC and all folders within ~30s." });
    }

    /// <summary>
    /// Trigger VNC installation on all online devices that don't have VNC installed.
    /// </summary>
    [HttpPost("vnc-install-all")]
    public async Task<IActionResult> TriggerVncInstallAll()
    {
        var devices = await _dbContext.Devices
            .Where(d => d.Online && !d.VncInstalled)
            .Select(d => d.Id)
            .ToListAsync();

        foreach (var deviceId in devices)
        {
            _queue.Enqueue(new CommandDto
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                Type = CommandType.InstallVnc,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return Ok(new { message = $"VNC install queued for {devices.Count} devices" });
    }

    #endregion

    #region File Manager Endpoints

    /// <summary>
    /// List directory contents
    /// </summary>
    [HttpPost("files/list")]
    public IActionResult FileList([FromQuery] string deviceId, [FromQuery] string path)
    {
        if (string.IsNullOrEmpty(deviceId))
            return BadRequest("deviceId required");

        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = CommandType.FileList,
            Payload = path ?? "C:\\",
            CreatedAtUtc = DateTime.UtcNow
        });

        return Ok(new { commandId, message = "FileList command queued" });
    }

    /// <summary>
    /// Create a new folder
    /// </summary>
    [HttpPost("files/mkdir")]
    public IActionResult FolderCreate([FromQuery] string deviceId, [FromQuery] string path)
    {
        if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(path))
            return BadRequest("deviceId and path required");

        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = CommandType.FolderCreate,
            Payload = path,
            CreatedAtUtc = DateTime.UtcNow
        });

        return Ok(new { commandId, message = "FolderCreate command queued" });
    }

    /// <summary>
    /// Delete file or folder
    /// </summary>
    [HttpDelete("files")]
    public IActionResult FileDelete([FromQuery] string deviceId, [FromQuery] string path)
    {
        if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(path))
            return BadRequest("deviceId and path required");

        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = CommandType.FileDelete,
            Payload = path,
            CreatedAtUtc = DateTime.UtcNow
        });

        return Ok(new { commandId, message = "FileDelete command queued" });
    }

    /// <summary>
    /// Upload file (content as base64)
    /// </summary>
    [HttpPost("files/upload")]
    public IActionResult FileUpload([FromQuery] string deviceId, [FromBody] FileUploadRequest request)
    {
        if (string.IsNullOrEmpty(deviceId) || request == null || string.IsNullOrEmpty(request.Path))
            return BadRequest("deviceId and path required");

        var commandId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new { path = request.Path, content = request.Content ?? "" });
        
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = CommandType.FileWrite,
            Payload = payload,
            CreatedAtUtc = DateTime.UtcNow
        });

        return Ok(new { commandId, message = "FileUpload command queued" });
    }

    /// <summary>
    /// Download file (returns base64 content via command result)
    /// </summary>
    [HttpPost("files/download")]
    public IActionResult FileDownload([FromQuery] string deviceId, [FromQuery] string path)
    {
        if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(path))
            return BadRequest("deviceId and path required");

        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = CommandType.FileRead,
            Payload = path,
            CreatedAtUtc = DateTime.UtcNow
        });

        return Ok(new { commandId, message = "FileDownload command queued" });
    }

    /// <summary>
    /// Tüm komut geçmişini döndürür (İşlem Geçmişi sayfası için)
    /// </summary>
    [HttpGet("command-history")]
    public async Task<IActionResult> GetCommandHistory([FromQuery] int limit = 200)
    {
        var records = await _dbContext.CommandResultRecords
            .OrderByDescending(r => r.CompletedAtUtc)
            .Take(limit)
            .Join(
                _dbContext.Devices,
                r => r.DeviceId,
                d => d.Id,
                (r, d) => new
                {
                    commandId      = r.CommandId,
                    deviceId       = r.DeviceId,
                    hostname       = d.Hostname,
                    type           = r.CommandType,
                    typeName       = r.CommandType == CommandType.Reboot        ? "Yeniden Başlat"
                                  : r.CommandType == CommandType.Shutdown       ? "Kapat"
                                  : r.CommandType == CommandType.ExecuteScript  ? "Script Çalıştır"
                                  : r.CommandType == CommandType.ListServices   ? "Servisleri Listele"
                                  : r.CommandType == CommandType.FolderCleanup  ? "Klasör Temizle"
                                  : r.CommandType == CommandType.UpdateAgent    ? "Agent Güncelle"
                                  : r.CommandType == CommandType.FileWrite      ? "Dosya Yükle"
                                  : r.CommandType == CommandType.FileDelete     ? "Dosya Sil"
                                  : r.CommandType == CommandType.InstallVnc    ? "VNC Kur"
                                  : r.CommandType == CommandType.UninstallAgent ? "Agent Kaldır"
                                  : "Diğer",
                    success        = r.Success,
                    completedAtUtc = r.CompletedAtUtc,
                    outputSnippet  = r.Output.Length > 120 ? r.Output.Substring(0, 120) + "..." : r.Output,
                }
            )
            .ToListAsync();

        return Ok(records);
    }

    /// <summary>
    /// Get command result by ID (for file operations)
    /// </summary>
    [HttpGet("command-results/{commandId}")]
    public async Task<ActionResult<CommandResultRecord>> GetCommandResult(Guid commandId)
    {
        var result = await _dbContext.CommandResultRecords
            .FirstOrDefaultAsync(r => r.CommandId == commandId);

        if (result == null)
            return NotFound("Command result not found or pending");

        return Ok(result);
    }

    #endregion
}

public class FileUploadRequest
{
    public string? Path { get; set; }
    public string? Content { get; set; } // Base64 encoded
}

public class VncStatusReport
{
    public string? DeviceId { get; set; }
    public bool Installed { get; set; }
    public string? Password { get; set; }
    public int Port { get; set; } = 5900;
}
