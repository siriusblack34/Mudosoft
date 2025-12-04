using System.Collections.Generic;

namespace Mudosoft.Shared.Dtos
{
    public class DeviceDetailDto
    {
        public string? Id { get; set; }
        public string? Hostname { get; set; }
        public string? IpAddress { get; set; }

        public OsInfoDto? Os { get; set; }

        public int Store { get; set; }
        public string? AgentVersion { get; set; }
        public string? Type { get; set; }
        public bool Online { get; set; }
        public string? LastSeen { get; set; }

        public int CpuUsage { get; set; }
        public int RamUsage { get; set; }
        public int DiskUsage { get; set; }

        public string? SqlVersion { get; set; }
        public string? PosVersion { get; set; }
        public bool Agent { get; set; }

        public IEnumerable<DeviceMetricDto>? Metrics { get; set; }
    }
}
