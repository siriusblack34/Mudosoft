namespace MudoSoft.Backend.Models;

public enum DeviceType
{
    POS,
    PC
}

public class Device
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public int StoreCode { get; set; }
    public string? StoreName { get; set; }
    public DeviceType Type { get; set; }

    public string? SqlVersion { get; set; }
    public string? PosVersion { get; set; }

    public bool Online { get; set; }
    public DateTime? LastSeen { get; set; }

    public int? CpuUsage { get; set; }
    public int? RamUsage { get; set; }
    public int? DiskUsage { get; set; }

    public string? AgentVersion { get; set; }
    public string HealthStatus { get; set; } = "Unknown"; // Healthy, Warning, Critical
    public int HealthScore { get; set; } = 100;

}
