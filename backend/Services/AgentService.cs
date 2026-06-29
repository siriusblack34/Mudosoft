using Orchestra.Shared.Dtos;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Backend.Crypto;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using Orchestra.Shared.Enums;

namespace Orchestra.Backend.Services;

public class AgentService : IAgentService
{
    private readonly CommandQueue _queue;
    private readonly ILogger<AgentService> _logger;
    private readonly OrchestraDbContext _dbContext;
    private readonly RsaKeyProvider _rsa;

    public AgentService(
        CommandQueue queue,
        ILogger<AgentService> logger,
        OrchestraDbContext dbContext,
        RsaKeyProvider rsa)
    {
        _queue = queue;
        _logger = logger;
        _dbContext = dbContext;
        _rsa = rsa;
    }

    public async Task HandleHeartbeatAsync(DeviceHeartbeatDto dto, string? remoteSourceIp = null)
    {
        _logger.LogInformation("Heartbeat from {DeviceId} CPU:{Cpu} RAM:{Ram} DISK:{Disk} | User:{User} BootTime:{Boot} | ReportedIp:{Reported} RemoteIp:{Remote}",
            dto.DeviceId, dto.CpuUsage, dto.RamUsage, dto.DiskUsage,
            dto.LastLoggedInUser ?? "(null)", dto.UptimeSince, dto.IpAddress, remoteSourceIp ?? "(null)");

        var device = await _dbContext.Devices.FindAsync(dto.DeviceId);

        if (device == null)
        {
            // Cihaz silinmis ama UninstallAgent komutu hala kuyrukta bekliyorsa,
            // aradaki heartbeat ile cihazi yeniden olusturma — listeden temizli kalmali.
            if (_queue.HasPendingCommand(dto.DeviceId, CommandType.UninstallAgent))
            {
                _logger.LogInformation("Heartbeat {DeviceId} icin tombstone — UninstallAgent pending, cihaz yeniden olusturulmadi.", dto.DeviceId);
                return;
            }

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
        if (!string.IsNullOrWhiteSpace(remoteSourceIp))
            device.RemoteSourceIp = remoteSourceIp;
        device.Online = true;
        device.LastSeen = DateTime.UtcNow;

        // Merkez agent kendisini CentralOffice olarak bildirir; bir kez set edildikten sonra değiştirilmez.
        if (!string.IsNullOrEmpty(dto.DeviceTypeName)
            && Enum.TryParse<DeviceType>(dto.DeviceTypeName, out var parsedType)
            && device.Type == DeviceType.Unknown)
        {
            device.Type = parsedType;
        }

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

    public async Task<List<CommandDto>> GetCommandsAsync(string deviceId)
    {
        var cmds = _queue.DequeueByDevice(deviceId);
        if (cmds.Count == 0)
            return cmds;

        // 🔒 Faz 2 (K-2): her komutu backend özel anahtarıyla imzala (ADDITIVE — eski agent'lar yok sayar).
        // Per-device monotonik Seq (replay koruması) DeviceCredential'da kalıcı tutulur.
        var cred = await _dbContext.DeviceCredentials.FindAsync(deviceId);
        if (cred == null)
        {
            cred = new DeviceCredential { DeviceId = deviceId, CreatedAtUtc = DateTime.UtcNow, LastCommandSeq = 0 };
            _dbContext.DeviceCredentials.Add(cred);
        }

        var now = DateTime.UtcNow;
        foreach (var cmd in cmds)
        {
            cred.LastCommandSeq++;
            cmd.Seq = cred.LastCommandSeq;
            cmd.IssuedAtUtc = now;
            cmd.ExpiresAtUtc = now.AddMinutes(5);
            cmd.Signature = Convert.ToBase64String(_rsa.Sign(CanonicalCommandBytes(cmd)));
        }
        cred.LastSeenAtUtc = now;
        await _dbContext.SaveChangesAsync();

        return cmds;
    }

    // Komut imzası için kanonik bayt dizisi — agent tarafı AYNI formatı üretip doğrular.
    internal static byte[] CanonicalCommandBytes(CommandDto c) =>
        Encoding.UTF8.GetBytes(
            $"{c.Id}|{c.DeviceId}|{(int)c.Type}|{c.Payload ?? ""}|{c.Seq}|{c.IssuedAtUtc:O}|{c.ExpiresAtUtc:O}");

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
        if (record.CommandType == Orchestra.Shared.Enums.CommandType.InstallVnc
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
