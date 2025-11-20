namespace MudoSoft.Backend.Models
{
    public class DeviceMetric
    {
        public long Id { get; set; }

        public string DeviceId { get; set; } = default!;
        public Device Device { get; set; } = default!;

        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public int CpuUsagePercent { get; set; }
        public int RamUsagePercent { get; set; }
        public int DiskUsagePercent { get; set; }

        // Future expansion:
        public double? CpuTemperature { get; set; }
        public double? DiskFreeGb { get; set; }
    }
}
