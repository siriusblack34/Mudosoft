using Microsoft.AspNetCore.Mvc;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceRepository _repo;

    public DevicesController(IDeviceRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Dashboard statistics: online/offline counts & recent offline list
    /// GET: /api/devices/status
    /// </summary>
    [HttpGet("status")]
    public ActionResult<DashboardDto> GetStatus()
    {
        var devices = _repo.GetAll() ?? new List<Device>();

        var total = devices.Count;
        var online = devices.Count(d => d.Online);
        var offline = total - online;

        var recentOffline = devices
            .Where(d => !d.Online)
            .OrderByDescending(d => d.LastSeen)
            .Take(10)
            .Select(d => new RecentOfflineDevice
            {
                Hostname = d.Hostname ?? "-",
                Ip = d.IpAddress,
                Os = d.Os ?? "-",
                Store = d.StoreCode,
                LastSeen = d.LastSeen?.ToString("g") ?? "-"
            })
            .ToList();

        return Ok(new DashboardDto
        {
            TotalDevices = total,
            Online = online,
            Offline = offline,
            RecentOffline = recentOffline
        });
    }

    /// <summary>
    /// Full devices inventory list
    /// GET: /api/devices/inventory
    /// </summary>
    [HttpGet("inventory")]
    public ActionResult<IEnumerable<Device>> GetInventory()
    {
        return Ok(_repo.GetAll());
    }

    /// <summary>
    /// Returns a device by ID
    /// GET: /api/devices/{id}
    /// </summary>
    [HttpGet("{id}")]
    public ActionResult<Device> GetById(string id)
    {
        var device = _repo.GetById(id);
        if (device is null)
            return NotFound($"Device not found: {id}");

        return Ok(device);
    }
}
