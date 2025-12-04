namespace Mudosoft.Shared.Dtos
{
    public class DeviceListDto
    {
        public string? Id { get; set; }
        public string? Hostname { get; set; }
        public string? IpAddress { get; set; }

        public OsInfoDto? Os { get; set; }

        public int StoreCode { get; set; }
        public string? Type { get; set; }
        public bool Online { get; set; }
        public string? LastSeen { get; set; }

        public int? CpuUsage { get; set; }
        public int? RamUsage { get; set; }
        public int? DiskUsage { get; set; }
    }
}
