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

    // Performance Metrics
    public double CpuUsage { get; set; }
    public double RamUsage { get; set; }
    public double DiskUsage { get; set; }

    // Hardware Inventory
    public string? CpuModel { get; set; }
    public long TotalRamMB { get; set; }
    public long TotalDiskGB { get; set; }
    public string? GpuModel { get; set; }

    // User & Session Info
    public string? LastLoggedInUser { get; set; }

    // System State
    public DateTime UptimeSince { get; set; } // Boot time for uptime calculation
    public bool Online { get; set; }

    // Agent Info
    public string? AgentVersion { get; set; } 

    public AgentCapabilities Capabilities { get; set; } = new();
}
