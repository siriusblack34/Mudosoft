using Mudosoft.Shared.Models;

namespace Mudosoft.Agent.Models;

public class HeartbeatRequest
{
    public string DeviceId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string? StoreCode { get; set; }
    public string OsVersion { get; set; } = "";
    public string PosVersion { get; set; } = "";
    public string SqlVersion { get; set; } = "";

    public double CpuUsage { get; set; }
    public double RamUsage { get; set; }
    public double DiskUsage { get; set; }

    public DateTime UptimeSince { get; set; }

    public AgentCapabilities Capabilities { get; set; } = new();
}
