using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mudosoft.Shared.Dtos;
using Mudosoft.Shared.Enums;
using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _service;
    private readonly CommandQueue _queue;
    private readonly ILogger<AgentController> _logger;
    private readonly MudoSoftDbContext _dbContext;

    public AgentController(
        IAgentService service,
        CommandQueue queue,
        ILogger<AgentController> logger,
        MudoSoftDbContext dbContext)
    {
        _service = service;
        _queue = queue;
        _logger = logger;
        _dbContext = dbContext;
    }

    // ❤️ Heartbeat (decrypt edilmiş DTO middleware'den gelir)
    // 🔥 DÜZELTME: Endpoint adı 'report' olarak değiştirildi (AgentWorker ile eşleşmesi için)
    [HttpPost("report")] 
    public async Task<IActionResult> Heartbeat([FromBody] DeviceHeartbeatDto dto)
    {
        if (dto == null)
            return BadRequest("DTO null");

        await _service.HandleHeartbeatAsync(dto);
        return Ok();
    }


    // 📥 Commands Poll
    [HttpGet("commands")]
    public async Task<ActionResult<List<CommandDto>>> GetCommands([FromQuery] string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest("deviceId required");

        var cmds = await _service.GetCommandsAsync(deviceId);
        return Ok(cmds);
    }

    // 📤 Command Result
    [HttpPost("command-result")]
    public async Task<IActionResult> CommandResult([FromBody] CommandResultDto result)
    {
        await _service.HandleCommandResultAsync(result);
        return Ok();
    }

    // 🚨 Events
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