using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/playbooks")]
public class RemediationPlaybooksController : ControllerBase
{
    private static readonly string[] AllowedTriggers = ["ServiceDown", "DeviceOffline", "CpuHigh", "DiskFull", "HealthScoreLow", "MemoryHigh", "AgentSilent"];
    private static readonly string[] AllowedActions = ["RestartService", "RunScript", "SendAlert", "Wait", "KillProcess", "RestartDevice", "ClearTempFiles"];

    private readonly OrchestraDbContext _db;
    private readonly IPlaybookEngine _engine;

    public RemediationPlaybooksController(OrchestraDbContext db, IPlaybookEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlaybookListItemDto>>> GetPlaybooks()
    {
        var playbooks = await _db.RemediationPlaybooks
            .AsNoTracking()
            .Include(p => p.Steps)
            .Include(p => p.Executions)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new PlaybookListItemDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                TriggerType = p.TriggerType,
                IsEnabled = p.IsEnabled,
                CreatedBy = p.CreatedBy,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                StepCount = p.Steps.Count,
                LastExecutionAt = p.Executions.Any() ? p.Executions.Max(e => e.StartedAt) : null,
                LastExecutionStatus = p.Executions.Any()
                    ? p.Executions.OrderByDescending(e => e.StartedAt).First().Status
                    : null
            })
            .ToListAsync();

        return Ok(playbooks);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PlaybookDetailDto>> GetPlaybook(int id)
    {
        var playbook = await _db.RemediationPlaybooks
            .AsNoTracking()
            .Include(p => p.Steps.OrderBy(s => s.StepOrder))
            .Include(p => p.Executions.OrderByDescending(e => e.StartedAt).Take(20))
            .FirstOrDefaultAsync(p => p.Id == id);

        if (playbook == null) return NotFound();

        return Ok(ToDetailDto(playbook));
    }

    [HttpPost]
    public async Task<ActionResult<PlaybookDetailDto>> CreatePlaybook([FromBody] PlaybookRequest request)
    {
        var err = ValidateRequest(request);
        if (err != null) return BadRequest(new { error = err });

        var playbook = new RemediationPlaybook
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            TriggerType = request.TriggerType,
            TriggerConditionJson = request.TriggerConditionJson,
            IsEnabled = request.IsEnabled,
            CreatedBy = GetCurrentUser(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        for (int i = 0; i < request.Steps.Count; i++)
        {
            var s = request.Steps[i];
            playbook.Steps.Add(new PlaybookStep
            {
                StepOrder = i + 1,
                ActionType = s.ActionType,
                ActionPayloadJson = s.ActionPayloadJson,
                DelaySeconds = s.DelaySeconds,
                Description = s.Description?.Trim()
            });
        }

        _db.RemediationPlaybooks.Add(playbook);
        await _db.SaveChangesAsync();

        var created = await _db.RemediationPlaybooks
            .Include(p => p.Steps.OrderBy(s => s.StepOrder))
            .Include(p => p.Executions)
            .FirstAsync(p => p.Id == playbook.Id);

        return CreatedAtAction(nameof(GetPlaybook), new { id = created.Id }, ToDetailDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PlaybookDetailDto>> UpdatePlaybook(int id, [FromBody] PlaybookRequest request)
    {
        var err = ValidateRequest(request);
        if (err != null) return BadRequest(new { error = err });

        var playbook = await _db.RemediationPlaybooks
            .Include(p => p.Steps)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (playbook == null) return NotFound();

        playbook.Name = request.Name.Trim();
        playbook.Description = request.Description?.Trim();
        playbook.TriggerType = request.TriggerType;
        playbook.TriggerConditionJson = request.TriggerConditionJson;
        playbook.IsEnabled = request.IsEnabled;
        playbook.UpdatedAt = DateTime.UtcNow;

        // Steps'i baştan yaz
        _db.PlaybookSteps.RemoveRange(playbook.Steps);
        playbook.Steps.Clear();

        for (int i = 0; i < request.Steps.Count; i++)
        {
            var s = request.Steps[i];
            playbook.Steps.Add(new PlaybookStep
            {
                StepOrder = i + 1,
                ActionType = s.ActionType,
                ActionPayloadJson = s.ActionPayloadJson,
                DelaySeconds = s.DelaySeconds,
                Description = s.Description?.Trim()
            });
        }

        await _db.SaveChangesAsync();

        var updated = await _db.RemediationPlaybooks
            .Include(p => p.Steps.OrderBy(s => s.StepOrder))
            .Include(p => p.Executions.OrderByDescending(e => e.StartedAt).Take(20))
            .FirstAsync(p => p.Id == id);

        return Ok(ToDetailDto(updated));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePlaybook(int id)
    {
        var playbook = await _db.RemediationPlaybooks.FirstOrDefaultAsync(p => p.Id == id);
        if (playbook == null) return NotFound();
        _db.RemediationPlaybooks.Remove(playbook);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:int}/toggle")]
    public async Task<IActionResult> ToggleEnabled(int id)
    {
        var playbook = await _db.RemediationPlaybooks.FirstOrDefaultAsync(p => p.Id == id);
        if (playbook == null) return NotFound();
        playbook.IsEnabled = !playbook.IsEnabled;
        playbook.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { playbook.IsEnabled });
    }

    // POST /api/playbooks/{id}/run — manuel tetikleme
    [HttpPost("{id:int}/run")]
    public async Task<IActionResult> RunPlaybook(int id, [FromBody] ManualRunRequest request)
    {
        var playbook = await _db.RemediationPlaybooks
            .Include(p => p.Steps.OrderBy(s => s.StepOrder))
            .FirstOrDefaultAsync(p => p.Id == id);

        if (playbook == null) return NotFound();

        _ = Task.Run(() => _engine.ExecuteAsync(playbook, request.DeviceId, request.Hostname,
            request.StoreCode, $"Manuel tetikleme: {GetCurrentUser()}"));

        return Ok(new { message = "Playbook çalıştırılıyor." });
    }

    // GET /api/playbooks/{id}/executions
    [HttpGet("{id:int}/executions")]
    public async Task<ActionResult<IEnumerable<PlaybookExecution>>> GetExecutions(int id, [FromQuery] int limit = 50)
    {
        var executions = await _db.PlaybookExecutions
            .AsNoTracking()
            .Where(e => e.PlaybookId == id)
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .ToListAsync();

        return Ok(executions);
    }

    private static string? ValidateRequest(PlaybookRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "İsim zorunludur.";
        if (req.Name.Trim().Length > 200) return "İsim en fazla 200 karakter olabilir.";
        if (!AllowedTriggers.Contains(req.TriggerType)) return "Geçersiz tetikleyici türü.";
        if (req.Steps.Count == 0) return "En az bir adım gereklidir.";
        foreach (var step in req.Steps)
        {
            if (!AllowedActions.Contains(step.ActionType)) return $"Geçersiz aksiyon türü: {step.ActionType}";
        }
        return null;
    }

    private string GetCurrentUser()
    {
        var fullName = User.FindFirstValue("fullName")?.Trim();
        return !string.IsNullOrWhiteSpace(fullName) ? fullName : User.FindFirstValue(ClaimTypes.Name) ?? "BT";
    }

    private static PlaybookDetailDto ToDetailDto(RemediationPlaybook p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        TriggerType = p.TriggerType,
        TriggerConditionJson = p.TriggerConditionJson,
        IsEnabled = p.IsEnabled,
        CreatedBy = p.CreatedBy,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        Steps = p.Steps.OrderBy(s => s.StepOrder).Select(s => new PlaybookStepDto
        {
            Id = s.Id,
            StepOrder = s.StepOrder,
            ActionType = s.ActionType,
            ActionPayloadJson = s.ActionPayloadJson,
            DelaySeconds = s.DelaySeconds,
            Description = s.Description
        }).ToList(),
        RecentExecutions = p.Executions.OrderByDescending(e => e.StartedAt).Take(20).Select(e => new PlaybookExecutionDto
        {
            Id = e.Id,
            DeviceId = e.DeviceId,
            Hostname = e.Hostname,
            StoreCode = e.StoreCode,
            Status = e.Status,
            StartedAt = e.StartedAt,
            CompletedAt = e.CompletedAt,
            ResultSummary = e.ResultSummary,
            TriggerReason = e.TriggerReason
        }).ToList()
    };
}

// DTOs
public class PlaybookListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string TriggerType { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int StepCount { get; set; }
    public DateTime? LastExecutionAt { get; set; }
    public string? LastExecutionStatus { get; set; }
}

public class PlaybookDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string TriggerType { get; set; } = "";
    public string? TriggerConditionJson { get; set; }
    public bool IsEnabled { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<PlaybookStepDto> Steps { get; set; } = [];
    public List<PlaybookExecutionDto> RecentExecutions { get; set; } = [];
}

public class PlaybookStepDto
{
    public int Id { get; set; }
    public int StepOrder { get; set; }
    public string ActionType { get; set; } = "";
    public string? ActionPayloadJson { get; set; }
    public int DelaySeconds { get; set; }
    public string? Description { get; set; }
}

public class PlaybookExecutionDto
{
    public int Id { get; set; }
    public string? DeviceId { get; set; }
    public string? Hostname { get; set; }
    public string? StoreCode { get; set; }
    public string Status { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ResultSummary { get; set; }
    public string TriggerReason { get; set; } = "";
}

public class PlaybookRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string TriggerType { get; set; } = "";
    public string? TriggerConditionJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<PlaybookStepRequest> Steps { get; set; } = [];
}

public class PlaybookStepRequest
{
    public string ActionType { get; set; } = "";
    public string? ActionPayloadJson { get; set; }
    public int DelaySeconds { get; set; } = 0;
    public string? Description { get; set; }
}

public class ManualRunRequest
{
    public string? DeviceId { get; set; }
    public string? Hostname { get; set; }
    public string? StoreCode { get; set; }
}
