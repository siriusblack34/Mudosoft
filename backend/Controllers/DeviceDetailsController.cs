using Microsoft.AspNetCore.Mvc;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;
using System;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/devices")]
[ApiExplorerSettings(GroupName = "v1")]
public class DeviceDetailsController : ControllerBase
{
    private readonly IDeviceRepository _repo;
    private readonly IAgentDataCache _agent;
    private readonly IWmiSystemInfoService _wmi;
    private readonly IPosVersionReader _pos;

    public DeviceDetailsController(
        IDeviceRepository repo,
        IAgentDataCache agent,
        IWmiSystemInfoService wmi,
        IPosVersionReader pos)
    {
        _repo = repo;
        _agent = agent;
        _wmi = wmi;
        _pos = pos;
    }

    [HttpGet("")]
    public IActionResult GetAll()
    {
        return Ok(_repo.GetAll());
    }

    [HttpGet("{id}/details")]
    public async Task<IActionResult> GetDetails(string id)
    {
        var device = _repo.GetById(id);
        if (device is null)
            return NotFound($"Device not found: {id}");

        var result = new DeviceDetailsDto
        {
            Id       = device.Id,
            Hostname = device.Hostname,
            Ip       = device.IpAddress,
            Store    = device.StoreCode,
            Type     = device.Type,
            Online   = device.Online,
            LastSeen = device.LastSeen,
            Os       = device.Os
        };

        // 1️⃣ Agent Data varsa kullan
        if (_agent.TryGet(device.IpAddress, out var agentInfo))
        {
            result.Agent = true;
            result.Cpu  = agentInfo?.CpuUsage != null ? (int?)Math.Round(agentInfo.CpuUsage.Value) : null;
            result.Ram  = agentInfo?.RamUsage != null ? (int?)Math.Round(agentInfo.RamUsage.Value) : null;
            result.Disk = agentInfo?.DiskUsage != null ? (int?)Math.Round(agentInfo.DiskUsage.Value) : null;
            result.SqlVersion = agentInfo?.SqlVersion;
            result.PosVersion = agentInfo?.PosVersion;
            return Ok(result);
        }

        // 2️⃣ POS Kasa tespiti (.31 / .32 / .33 veya enum POS)
        bool isPos =
            device.Type == DeviceType.POS ||
            device.IpAddress.EndsWith(".31") ||
            device.IpAddress.EndsWith(".32") ||
            device.IpAddress.EndsWith(".33");

        if (isPos)
        {
            Console.WriteLine($"[POS] POS detected for {device.IpAddress}");
            result.PosVersion = await _pos.GetVersion(device.IpAddress);
        }

        // 3️⃣ PC ise WMI ile sistem bilgisi çek
        if (!isPos && device.Type == DeviceType.PC && device.Online)
        {
            var wmiInfo = await _wmi.GetSystemInfo(device.IpAddress);
            if (wmiInfo != null)
            {
                result.Cpu  = wmiInfo.CpuUsage != null ? (int?)Math.Round(wmiInfo.CpuUsage.Value) : null;
                result.Ram  = wmiInfo.RamUsage != null ? (int?)Math.Round(wmiInfo.RamUsage.Value) : null;
                result.Disk = wmiInfo.DiskUsage != null ? (int?)Math.Round(wmiInfo.DiskUsage.Value) : null;
                result.Os   = wmiInfo.Os ?? result.Os;
            }
        }

        return Ok(result);
    }
}
