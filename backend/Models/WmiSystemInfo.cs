namespace MudoSoft.Backend.Models
{
    public class WmiSystemInfo
    {
        public string? Os { get; set; }
        public double? CpuUsage { get; set; }
        public double? RamUsage { get; set; }
        public double? DiskUsage { get; set; }
        public string? SqlVersion { get; set; }
        public string? PosVersion { get; set; }
    }
}
