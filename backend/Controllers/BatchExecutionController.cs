using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orchestra.Backend.Middleware;
using Orchestra.Backend.Services;
using System.Security.Claims;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[RequireMenu("/batch-scripts")] // tüm uçlar Acil Bat menüsüne bağlı
[Route("api/batch")]
public class BatchExecutionController : ControllerBase
{
    private readonly BatchExecutionService _service;
    private readonly ActivityLogService _activity;
    private readonly ILogger<BatchExecutionController> _logger;

    public BatchExecutionController(
        BatchExecutionService service,
        ActivityLogService activity,
        ILogger<BatchExecutionController> logger)
    {
        _service = service;
        _activity = activity;
        _logger = logger;
    }

    /// <summary>
    /// Bat dosyasini secili cihazlarda calistirir.
    /// Agent'li cihazlar icin CommandQueue, agent'siz cihazlar icin WMI + admin share kullanir.
    /// </summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] BatchRunRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "Request bos" });

        var user = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "anonymous";

        try
        {
            var execution = await _service.StartAsync(request, user);

            await _activity.LogAsync(
                category: "BatchExecution",
                action: "Run",
                target: request.FileName,
                details: $"TargetCount={execution.Targets.Count}; Agent={execution.Targets.Count(t => t.Mode == BatchTargetMode.Agent)}; Agentless={execution.Targets.Count(t => t.Mode == BatchTargetMode.Agentless)}",
                ct: ct);

            return Accepted(new
            {
                executionId = execution.Id,
                fileName = execution.FileName,
                targets = execution.Targets.Select(t => new
                {
                    key = t.Key,
                    mode = t.Mode.ToString(),
                    deviceId = t.DeviceId,
                    ipAddress = t.IpAddress,
                    hostname = t.Hostname,
                    storeCode = t.StoreCode,
                    phase = t.Phase.ToString()
                })
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch run hatasi");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Belirli bir yurutmenin durumunu doner (her target icin output ve phase).
    /// </summary>
    [HttpGet("status/{executionId}")]
    public IActionResult Status(string executionId)
    {
        var execution = _service.Get(executionId);
        if (execution == null) return NotFound(new { error = "Execution bulunamadi" });

        return Ok(new
        {
            id = execution.Id,
            fileName = execution.FileName,
            createdBy = execution.CreatedBy,
            createdAtUtc = execution.CreatedAtUtc,
            targets = execution.Targets.Select(t => new
            {
                key = t.Key,
                mode = t.Mode.ToString(),
                deviceId = t.DeviceId,
                ipAddress = t.IpAddress,
                hostname = t.Hostname,
                storeCode = t.StoreCode,
                phase = t.Phase.ToString(),
                output = t.Output,
                error = t.Error,
                startedAtUtc = t.StartedAtUtc,
                completedAtUtc = t.CompletedAtUtc,
                commandId = t.CommandId
            })
        });
    }

    /// <summary>
    /// Aktif/gecmis yurutmelerin listesi (in-memory, restart'a kadar).
    /// </summary>
    [HttpGet("history")]
    public IActionResult History()
    {
        return Ok(_service.List().Select(e => new
        {
            id = e.Id,
            fileName = e.FileName,
            createdBy = e.CreatedBy,
            createdAtUtc = e.CreatedAtUtc,
            totalTargets = e.Targets.Count,
            done = e.Targets.Count(t => t.Phase == BatchPhase.Done),
            running = e.Targets.Count(t => t.Phase == BatchPhase.Running || t.Phase == BatchPhase.Pending),
            error = e.Targets.Count(t => t.Phase == BatchPhase.Error)
        }));
    }
}
