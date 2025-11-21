using Microsoft.AspNetCore.Mvc;
using Mudosoft.Shared.Dtos;
using Mudosoft.Shared.Enums;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActionsController : ControllerBase
{
    private readonly CommandQueue _queue;
    private readonly ILogger<ActionsController> _logger;

    public ActionsController(CommandQueue queue, ILogger<ActionsController> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    /// <summary>
    /// Belirtilen cihazı yeniden başlatma komutunu kuyruğa alır.
    /// </summary>
    [HttpPost("reboot")]
    public IActionResult Reboot([FromBody] ExecuteActionRequest request)
    {
        _queue.Enqueue(new CommandDto
        {
            Id = Guid.NewGuid(),
            DeviceId = request.DeviceId,
            Type = CommandType.Reboot,
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("Reboot komutu kuyruğa alındı: {DeviceId}", request.DeviceId);
        return Accepted();
    }

    /// <summary>
    /// Belirtilen cihazda script çalıştırma komutunu kuyruğa alır. (YENİ)
    /// </summary>
    [HttpPost("run-script")]
    public IActionResult RunScript([FromBody] ExecuteActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Script))
        {
            return BadRequest("Script içeriği boş olamaz.");
        }

        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = request.DeviceId,
            Type = CommandType.ExecuteScript, // Yeni komut tipi
            Payload = request.Script,        // Çalıştırılacak script
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("Script çalıştırma komutu kuyruğa alındı: {DeviceId}, CommandId: {CommandId}", 
                               request.DeviceId, commandId);
        
        // Komutun benzersiz kimliğini döndürerek Frontend'in sonucu takip etmesini sağlarız.
        return Accepted(new { commandId = commandId.ToString() }); 
    }
}