namespace MudoSoft.Backend.Models;

public class DeviceDetailsDto
{
    public string Id { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Store { get; set; }
    public DeviceType Type { get; set; }
    public bool Online { get; set; }
    public DateTime? LastSeen { get; set; }
    public string? Os { get; set; }
    public int? Cpu { get; set; }
    public int? Ram { get; set; }
    public int? Disk { get; set; }
    public string? SqlVersion { get; set; }
    public string? PosVersion { get; set; }
    public bool Agent { get; set; }
}
