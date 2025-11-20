using Mudosoft.Shared.Models;

namespace Mudosoft.Shared.Dtos;

public sealed class DeviceHeartbeatDto
{
    public string DeviceId { get; set; } = default!;
    public string? StoreCode { get; set; }
    public string Hostname { get; set; } = default!;
    public string IpAddress { get; set; } = default!;
    public string OsVersion { get; set; } = default!;
    public string PosVersion { get; set; } = default!;
    public string SqlVersion { get; set; } = default!;

    public double CpuUsage { get; set; }
    public double RamUsage { get; set; }
    public double DiskUsage { get; set; }

    public DateTime UptimeSince { get; set; }

    public bool Online { get; set; }

    public AgentCapabilities Capabilities { get; set; } = new();
}
