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

    // ✅ DÜZELTME: TimestampUtc = DateTime.UtcNow kullanılıyor
    public async Task HandleHeartbeatAsync(DeviceHeartbeatDto dto)
    {
        _logger.LogInformation("Heartbeat from {DeviceId} CPU:{Cpu} RAM:{Ram} DISK:{Disk}",
            dto.DeviceId, dto.CpuUsage, dto.RamUsage, dto.DiskUsage);

        var deviceExists = await _dbContext.Devices.AnyAsync(d => d.Id == dto.DeviceId);

        Device device;

        if (!deviceExists)
        {
            // YENİ CİHAZ
            device = new Device
            {
                Id = dto.DeviceId,
                FirstSeen = DateTime.UtcNow,
                Type = DeviceType.Unknown,
                Metrics = new List<DeviceMetric>()
            };
            _dbContext.Devices.Add(device);
        }
        else
        {
            // MEVCUT CİHAZ
            device = new Device { Id = dto.DeviceId };
            _dbContext.Devices.Attach(device);
            _dbContext.Entry(device).State = EntityState.Modified;
        }

        // Temel bilgiler güncellenir
        device.Hostname = dto.Hostname;
        device.IpAddress = dto.IpAddress;
        device.Online = true;
        device.LastSeen = DateTime.UtcNow; // ✅ Server UTC time
        device.Os = dto.OsVersion;
        device.PosVersion = dto.PosVersion;
        device.SqlVersion = dto.SqlVersion;
        device.StoreCode = int.TryParse(dto.StoreCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storeCode) ? storeCode : 0;

        // ✅ METRİK KAYDI: Server'ın UTC zamanını kullan
        var metric = new DeviceMetric
        {
            DeviceId = dto.DeviceId,
            TimestampUtc = DateTime.UtcNow, // 🔥 Agent'tan gelen değil, server time
            CpuUsagePercent = (int)Math.Round(dto.CpuUsage),
            RamUsagePercent = (int)Math.Round(dto.RamUsage),
            DiskUsagePercent = (int)Math.Round(dto.DiskUsage)
        };
        _dbContext.DeviceMetrics.Add(metric);

        // Sağlık durumu güncellenir
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

        _dbContext.CommandResults.Add(record);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Komut sonucu veritabanına kaydedildi. ID: {Id}", result.CommandId);
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