using Microsoft.AspNetCore.Mvc;
using Mudosoft.Shared.Dtos;
using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using Microsoft.EntityFrameworkCore;
using Mudosoft.Shared.Enums;

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

    // ❤️ Heartbeat
    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] DeviceHeartbeatDto dto)
    {
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

    // 📤 Command Result (agent geri gönderiyor)
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

    // 🏆 Ön Uca: Son Komut Sonucunu çekme
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
}
