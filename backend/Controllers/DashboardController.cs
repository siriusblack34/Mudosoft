using Microsoft.AspNetCore.Mvc;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDeviceRepository _deviceRepo;

    public DashboardController(IDeviceRepository deviceRepo)
    {
        _deviceRepo = deviceRepo;
    }

    [HttpGet]
    public IActionResult GetDashboard()
    {
        var devices = _deviceRepo.GetAll() ?? new List<Device>();

        var total   = devices.Count;
        var online  = devices.Count(d => d.Online);
        var offline = total - online;

        int healthy  = devices.Count(d => d.HealthStatus?.Equals("Healthy")  == true);
        int warning  = devices.Count(d => d.HealthStatus?.Equals("Warning")  == true);
        int critical = devices.Count(d => d.HealthStatus?.Equals("Critical") == true);

        var recentOffline = devices
            .Where(d => !d.Online)
            .OrderByDescending(d => d.LastSeen)
            .Take(10)
            .Select(d => new RecentOfflineDevice
            {
                Hostname = d.Hostname,
                Ip       = d.IpAddress,
                Os       = d.Os ?? "Unknown",
                Store    = d.StoreCode,
                LastSeen = d.LastSeen?.ToString("g") ?? "-"
            })
            .ToList();

        return Ok(new DashboardDto
        {
            TotalDevices  = total,
            Online        = online,
            Offline       = offline,
            Healthy       = healthy,
            Warning       = warning,
            Critical      = critical,
            RecentOffline = recentOffline
        });
    }
}
