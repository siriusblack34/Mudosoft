using System;

namespace MudoSoft.Backend.Models
{
    public class AgentReport
    {
        public string Ip { get; set; } = "";
        public string Hostname { get; set; } = "";
        public int StoreCode { get; set; }

        public string? Os { get; set; }
        public string? SqlVersion { get; set; }
        public string? PosVersion { get; set; }

        public double? CpuUsage { get; set; }
        public double? RamUsage { get; set; }
        public double? DiskUsage { get; set; }

        public string? AgentVersion { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }
}
