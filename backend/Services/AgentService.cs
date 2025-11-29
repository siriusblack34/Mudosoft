// siriusblack34/mudosoft/Mudosoft-138a269b679ef64544ce6a0b899393e338ef513e/backend/Services/AgentService.cs

using Mudosoft.Shared.Dtos;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Mudosoft.Shared.Enums;
using System.Linq; // Math için eklendi
using System.Collections.Generic; // List için eklendi

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
        _logger.LogInformation("Heartbeat from {DeviceId} CPU:{Cpu} RAM:{Ram} DISK:{Disk}",
            dto.DeviceId, dto.CpuUsage, dto.RamUsage, dto.DiskUsage);

        var device = await _dbContext.Devices.FindAsync(dto.DeviceId);

        if (device == null)
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

        // Temel bilgiler güncellenir
        device.Hostname = dto.Hostname;
        device.IpAddress = dto.IpAddress;
        device.Online = true; 
        device.LastSeen = DateTime.UtcNow; 
        
        // 🚀 KRİTİK KAYIT: OS ve Agent Version'ı kaydet
        device.Os = dto.OsVersion;
        device.AgentVersion = dto.AgentVersion; // ✅ YENİ EKLENEN DTO ALANINDAN KAYIT

        device.PosVersion = dto.PosVersion;
        device.SqlVersion = dto.SqlVersion;
        device.StoreCode = int.TryParse(dto.StoreCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storeCode) ? storeCode : 0; 

        // 🟢 GÜNCELLEME: Canlı metrik alanlarını Device modeline kopyala. 
        device.CurrentCpuUsagePercent = (float)dto.CpuUsage;
        device.CurrentRamUsagePercent = (float)dto.RamUsage;
        device.CurrentDiskUsagePercent = (float)dto.DiskUsage;

        // METRİK KAYDI: Server'ın UTC zamanını kullan
        var metric = new DeviceMetric
        {
            DeviceId = dto.DeviceId,
            TimestampUtc = DateTime.UtcNow,
            CpuUsagePercent = (int)System.Math.Round(dto.CpuUsage),
            RamUsagePercent = (int)System.Math.Round(dto.RamUsage),
            DiskUsagePercent = (int)System.Math.Round(dto.DiskUsage)
        };
        _dbContext.DeviceMetrics.Add(metric);

        // Sağlık durumu güncellenir
        UpdateDeviceHealth(device, metric);

        await _dbContext.SaveChangesAsync();
    }
    
    // ... (Diğer metotlar aynı kalır)
    
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
        device.HealthScore = System.Math.Max(0, score);
    }
}