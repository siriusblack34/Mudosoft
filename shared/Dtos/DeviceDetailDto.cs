using System.Collections.Generic;

namespace Orchestra.Shared.Dtos
{
    public class DeviceDetailDto
    {
        public string? Id { get; set; }
        public string? Hostname { get; set; }
        public string? IpAddress { get; set; }

        public OsInfoDto? Os { get; set; }

        public int StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? AgentVersion { get; set; }
        public string? Type { get; set; }
        public bool Online { get; set; }
        public bool ExcludeFromOfflineList { get; set; }
        public bool IsTemporarilyClosed { get; set; }
        public string? TemporaryCloseReason { get; set; }
        public string? LastSeen { get; set; }
        public string? FirstSeen { get; set; }

        // Live Metrics
        public int CpuUsage { get; set; }
        public int RamUsage { get; set; }
        public int DiskUsage { get; set; }

        // Hardware Inventory
        public string? CpuModel { get; set; }
        public long TotalRamMB { get; set; }
        public long TotalDiskGB { get; set; }
        public string? GpuModel { get; set; }

        // User & Session
        public string? LastLoggedInUser { get; set; }

        // Uptime
        public string? SystemBootTime { get; set; }

        public string? SqlVersion { get; set; }
        public string? PosVersion { get; set; }
        public bool Agent { get; set; }

        // VNC (Web Remote Desktop)
        public bool VncInstalled { get; set; }
        public int VncPort { get; set; }

        public IEnumerable<DeviceMetricDto>? Metrics { get; set; }
    }
}

