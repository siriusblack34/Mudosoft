namespace Mudosoft.Shared.Dtos
{
    public class DeviceMetricDto
    {
        public string? TimestampUtc { get; set; }
        public float CpuUsagePercent { get; set; }
        public float RamUsagePercent { get; set; }
        public float DiskUsagePercent { get; set; }
    }
}
