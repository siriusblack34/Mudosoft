using Microsoft.AspNetCore.Mvc;
using MudoSoft.Backend.Services;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly IAlertRepository _repo;

    public AlertsController(IAlertRepository repo)
    {
        _repo = repo;
    }

    [HttpGet]
    public IActionResult GetAll() => Ok(_repo.GetAll());
}
