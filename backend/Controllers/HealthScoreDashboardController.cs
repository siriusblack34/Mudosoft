using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orchestra.Backend.Services;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/health-score")]
public class HealthScoreDashboardController : ControllerBase
{
    private readonly HealthScoreService _healthScore;
    private readonly ILogger<HealthScoreDashboardController> _logger;

    public HealthScoreDashboardController(HealthScoreService healthScore, ILogger<HealthScoreDashboardController> logger)
    {
        _healthScore = healthScore;
        _logger = logger;
    }

    // GET /api/health-score/summary
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var summary = await _healthScore.GetSummaryAsync();
        return Ok(summary);
    }

    // GET /api/health-score/critical
    [HttpGet("critical")]
    public async Task<IActionResult> GetCritical()
    {
        var devices = await _healthScore.GetCriticalDevicesAsync();
        return Ok(devices);
    }

    // GET /api/health-score/risky
    [HttpGet("risky")]
    public async Task<IActionResult> GetRisky()
    {
        var devices = await _healthScore.GetRiskyDevicesAsync();
        return Ok(devices);
    }

    // GET /api/health-score/device/{deviceId}
    [HttpGet("device/{deviceId}")]
    public async Task<IActionResult> GetDevice(string deviceId)
    {
        try
        {
            var breakdown = await _healthScore.GetDeviceScoreAsync(deviceId);
            return Ok(breakdown);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
