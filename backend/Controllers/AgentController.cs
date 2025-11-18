using Microsoft.AspNetCore.Mvc;
using Mudosoft.Shared.Dtos;
using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _service;
    private readonly CommandQueue _queue;
    private readonly ILogger<AgentController> _logger;

    public AgentController(IAgentService service, CommandQueue queue, ILogger<AgentController> logger)
    {
        _service = service;
        _queue = queue;
        _logger = logger;
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

    // 📤 Command Result
    [HttpPost("command-result")]
    public async Task<IActionResult> CommandResult([FromBody] CommandResultDto result)
    {
        await _service.HandleCommandResultAsync(result);
        return Ok();
    }

    // 🚨 Events (POS Freeze, Disk High, Printer Offline vs.)
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
            Type = Mudosoft.Shared.Enums.CommandType.Reboot,
            CreatedAtUtc = DateTime.UtcNow
        });

        return Ok("Test command queued.");
    }

    // 📝 Your previous logs API (retained)
    [HttpGet("logs/{deviceId}")]
    public ActionResult<IEnumerable<string>> GetLogs(string deviceId)
    {
        var logs = new List<string>
        {
            $"{DateTime.UtcNow.AddMinutes(-5):O} - Agent started for {deviceId}",
            $"{DateTime.UtcNow.AddMinutes(-1):O} - Heartbeat OK"
        };

        return Ok(logs);
    }
}
