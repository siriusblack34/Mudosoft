using Mudosoft.Shared.Dtos;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Mudosoft.Shared.Enums;

namespace MudoSoft.Backend.Services;

public class AgentService : IAgentService
{
    private readonly CommandQueue _queue;
    private readonly ILogger<AgentService> _logger;
    private readonly MudoSoftDbContext _dbContext;

    public AgentService(
        CommandQueue queue,
        ILogger<AgentService> logger,
        MudoSoftDbContext dbContext)
    {
        _queue = queue;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task HandleHeartbeatAsync(DeviceHeartbeatDto dto)
    {
        _logger.LogInformation("Heartbeat from {DeviceId} CPU:{Cpu} RAM:{Ram} DISK:{Disk} | User:{User} BootTime:{Boot}",
            dto.DeviceId, dto.CpuUsage, dto.RamUsage, dto.DiskUsage,
            dto.LastLoggedInUser ?? "(null)", dto.UptimeSince);

        var device = await _dbContext.Devices.FindAsync(dto.DeviceId);

        if (device == null)
        {
            device = new Device
            {
                Id = dto.DeviceId,
                FirstSeen = DateTime.UtcNow,
                Type = DeviceType.Unknown,
                Metrics = new List<DeviceMetric>()
            };
            _dbContext.Devices.Add(device);
        }

        // Basic Info
        device.Hostname = dto.Hostname;
        device.IpAddress = dto.IpAddress;
        device.Online = true;
        device.LastSeen = DateTime.UtcNow;

        // System Info
        device.Os = dto.OsVersion;
        device.AgentVersion = dto.AgentVersion;
        device.PosVersion = dto.PosVersion;
        device.SqlVersion = dto.SqlVersion;

        var storeMatch = await _dbContext.StoreDevices
            .AsNoTracking()
            .Where(sd => sd.CalculatedIpAddress == dto.IpAddress)
            .Select(sd => new { sd.StoreCode, sd.StoreName })
            .FirstOrDefaultAsync();

        if (storeMatch != null)
        {
            device.StoreCode = storeMatch.StoreCode;
            device.StoreName = storeMatch.StoreName;
        }
        else
        {
            device.StoreCode = int.TryParse(dto.StoreCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storeCode)
                ? storeCode
                : 0;
        }

        // Hardware Inventory
        device.CpuModel = dto.CpuModel;
        device.TotalRamMB = dto.TotalRamMB;
        device.TotalDiskGB = dto.TotalDiskGB;
        device.GpuModel = dto.GpuModel;

        // User & Session
        device.LastLoggedInUser = dto.LastLoggedInUser;

        // Uptime (boot time)
        device.SystemBootTime = dto.UptimeSince;

        // Live Metrics
        device.CurrentCpuUsagePercent = (float)dto.CpuUsage;
        device.CurrentRamUsagePercent = (float)dto.RamUsage;
        device.CurrentDiskUsagePercent = (float)dto.DiskUsage;

        // D Drive Metrics
        if (dto.DiskDUsage.HasValue)
        {
            device.CurrentDiskDUsagePercent = (float)dto.DiskDUsage.Value;
            device.TotalDiskDGB = dto.TotalDiskDGB;
        }

        // METRIC YAZ
        var metric = new DeviceMetric
        {
            DeviceId = dto.DeviceId,
            TimestampUtc = DateTime.UtcNow,
            CpuUsagePercent = (int)Math.Round(dto.CpuUsage),
            RamUsagePercent = (int)Math.Round(dto.RamUsage),
            DiskUsagePercent = (int)Math.Round(dto.DiskUsage)
        };

        _dbContext.DeviceMetrics.Add(metric);

        UpdateDeviceHealth(device, metric);

        await _dbContext.SaveChangesAsync();
    }

    public Task<List<CommandDto>> GetCommandsAsync(string deviceId)
    {
        var cmds = _queue.DequeueByDevice(deviceId);
        return Task.FromResult(cmds);
    }

    public async Task HandleCommandResultAsync(CommandResultDto result)
    {
        _logger.LogInformation("Command {CommandId} executed by {DeviceId} Success:{Success}",
            result.CommandId, result.DeviceId, result.Success);

        var record = new CommandResultRecord
        {
            CommandId = result.CommandId,
            DeviceId = result.DeviceId,
            CommandType = result.CommandType,
            Success = result.Success,
            Output = result.Output ?? "Çıktı yok.",
            CompletedAtUtc = DateTime.UtcNow
        };

        _dbContext.CommandResultRecords.Add(record);

        // Extract VNC password from InstallVnc command result (sent via SignalR)
        if (record.CommandType == Mudosoft.Shared.Enums.CommandType.InstallVnc
            && result.Success
            && result.Output?.Contains("VNC_PWD=") == true)
        {
            var pwdMatch = System.Text.RegularExpressions.Regex.Match(result.Output, @"VNC_PWD=(\S+)");
            if (pwdMatch.Success)
            {
                var device = await _dbContext.Devices.FindAsync(result.DeviceId);
                if (device != null)
                {
                    device.VncInstalled = true;
                    device.VncPassword = pwdMatch.Groups[1].Value;
                    device.VncPort = 5900;
                    _logger.LogInformation("[VNC] Password saved for device {DeviceId} via command result", result.DeviceId);
                }
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    public Task HandleEventAsync(DeviceEventDto evt)
    {
        _logger.LogWarning("Event from {DeviceId}: {Type} ({Severity}) {Details}",
            evt.DeviceId, evt.EventType, evt.Severity, evt.Details);

        return Task.CompletedTask;
    }

    private void UpdateDeviceHealth(Device device, DeviceMetric metric)
    {
        var score = 100;
        var status = "Healthy";

        if (metric.CpuUsagePercent > 90 || metric.RamUsagePercent > 95 || metric.DiskUsagePercent > 98)
        {
            status = "Critical";
            score = 0;
        }
        else if (metric.CpuUsagePercent > 70 || metric.RamUsagePercent > 85 || metric.DiskUsagePercent > 90)
        {
            status = "Warning";
            score -= 30;
        }

        device.HealthStatus = status;
        device.HealthScore = Math.Max(0, score);
    }
}
