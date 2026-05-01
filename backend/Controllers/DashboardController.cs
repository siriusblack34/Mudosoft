using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;
using Orchestra.Backend.Dtos;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDeviceRepository _deviceRepo;

    public DashboardController(IDeviceRepository deviceRepo)
    {
        _deviceRepo = deviceRepo;
    }

    [HttpGet("summary")] // 🔥 BURAYA EKLEDİK! Frontend'in istediği '/summary' yolunu sağlıyor.
    public IActionResult GetDashboard()
    {
        var devices = _deviceRepo.GetAll() ?? new List<Device>();

        var total   = devices.Count;
        var online  = devices.Count(d => d.Online);
        var offline = devices.Count(d => !d.Online && !IsIgnoredForOfflineTracking(d));

        int healthy   = devices.Count(d => d.HealthStatus?.Equals("Healthy")  == true);
        int warning   = devices.Count(d => d.HealthStatus?.Equals("Warning")  == true);
        int critical  = devices.Count(d => d.HealthStatus?.Equals("Critical") == true);

        var recentOffline = devices
            .Where(d => !d.Online && !IsIgnoredForOfflineTracking(d))
            .OrderByDescending(d => d.LastSeen)
            .Take(10)
            .Select(d => new RecentOfflineDeviceDto
            {
                Hostname = d.Hostname ?? "-",
                Ip       = d.IpAddress,
                Os       = d.Os ?? "Unknown",
                Store    = d.StoreCode,
                LastSeen = d.LastSeen?.ToString("g") ?? "-"
            })
            .ToList();

        return Ok(new DashboardResponseDto
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

    private static bool IsIgnoredForOfflineTracking(Device device)
    {
        return device.ExcludeFromOfflineList || device.IsTemporarilyClosed;
    }
}
