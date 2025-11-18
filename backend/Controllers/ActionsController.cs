using Microsoft.AspNetCore.Mvc;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActionsController : ControllerBase
{
    private readonly IActionRepository _actionRepo;
    private readonly IDeviceRepository _deviceRepo;

    public ActionsController(IActionRepository actionRepo, IDeviceRepository deviceRepo)
    {
        _actionRepo = actionRepo;
        _deviceRepo = deviceRepo;
    }

    // GET /api/actions
    [HttpGet]
    public ActionResult<IEnumerable<ActionRecord>> GetAll()
    {
        return Ok(_actionRepo.GetAll());
    }

    // GET /api/actions/device/{deviceId}
    [HttpGet("device/{deviceId}")]
    public ActionResult<IEnumerable<ActionRecord>> GetByDevice(string deviceId)
    {
        return Ok(_actionRepo.GetByDevice(deviceId));
    }

    // POST /api/actions/execute
    [HttpPost("execute")]
    public ActionResult<ActionRecord> Execute([FromBody] ExecuteActionRequest request)
    {
        var device = _deviceRepo.GetById(request.DeviceId);
        if (device is null)
            return BadRequest($"Device {request.DeviceId} not found.");

        var record = new ActionRecord
        {
            DeviceId = device.Id,
            DeviceHostname = device.Hostname,
            StoreCode = device.StoreCode,
            Type = request.ActionType,
            Status = "pending",
            RequestedBy = "Administrator",
            CreatedAt = DateTime.UtcNow,
            Summary = $"Queued {request.ActionType} for {device.Hostname}"
        };

        // Şimdilik sadece kayda alıyoruz; ileride gerçek job/agent entegrasyonu gelecek.
        record = _actionRepo.Add(record);

        return Ok(record);
    }

    // POST /api/actions/sql  (şimdilik stub, loglayıp OK dönsün)
    [HttpPost("sql")]
    public ActionResult<object> RunSql([FromBody] ExecuteActionRequest request)
    {
        // Gelecekte agent üzerinden gerçek SQL çalıştırılacak.
        // Şimdilik fake result döndürüyoruz.
        var result = new
        {
            columns = new[] { "Result" },
            rows = new object[][] { new object[] { "SQL execution stubbed." } }
        };

        return Ok(result);
    }
}
