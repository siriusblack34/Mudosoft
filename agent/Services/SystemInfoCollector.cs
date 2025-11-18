using System.Diagnostics;
using System.Management;
using Mudosoft.Agent.Options;
using Mudosoft.Shared.Dtos;
using Mudosoft.Shared.Models;
using Microsoft.Extensions.Options;

namespace Mudosoft.Agent.Services;

public sealed class SystemInfoCollector : ISystemInfoCollector
{
    private readonly AgentOptions _options;

    public SystemInfoCollector(IOptions<AgentOptions> options)
    {
        _options = options.Value;
    }

    public Task<DeviceHeartbeatDto> CollectAsync(CancellationToken cancellationToken)
    {
        // Burada şimdilik minimal bilgi toplayalım, sonra WMI detaylarını doldururuz.
        var hostname = Environment.MachineName;
        var ip = "0.0.0.0"; // TODO: gerçek IP’yi bul
        var os = Environment.OSVersion.ToString();

        // Basit CPU/RAM ölçümü (ileri seviye için PerformanceCounter/WMI geçebiliriz)
        double cpu = 0;
        double ram = 0;
        double disk = 0;

        var dto = new DeviceHeartbeatDto
        {
            DeviceId   = _options.DeviceId,
            StoreCode  = _options.StoreCode,
            Hostname   = hostname,
            IpAddress  = ip,
            OsVersion  = os,
            PosVersion = "unknown",
            SqlVersion = "unknown",
            CpuUsage   = cpu,
            RamUsage   = ram,
            DiskUsage  = disk,
            UptimeSince = GetSystemUptime()
        };

        dto.Capabilities = new AgentCapabilities
        {
            CanExecuteCommands   = true,
            HasWatchdogs         = true,
            SupportsSelfHealing  = true,
            SupportsPeripheralChecks = true,
            AgentVersion = "1.0.0"
        };

        return Task.FromResult(dto);
    }

    private static DateTime GetSystemUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return DateTime.UtcNow - uptime;
    }
}
