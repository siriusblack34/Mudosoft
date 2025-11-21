namespace MudoSoft.Backend.Models;

public class DeviceMetricHistoryDto
{
    public DateTime Timestamp { get; set; } 
    public int Cpu { get; set; }
    public int Ram { get; set; }
    public int Disk { get; set; }
}