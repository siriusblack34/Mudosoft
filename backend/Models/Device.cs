using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace Orchestra.Backend.Models
{
    public enum DeviceType
    {
        Unknown = 0,
        POS = 1,
        PC = 2,
        Server = 3
    }

    public class Device
    {
        // PRIMARY KEY
        [Key]
        [MaxLength(450)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // BASIC INFO
        public string Hostname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int StoreCode { get; set; }
        public string? StoreName { get; set; }
        public DeviceType Type { get; set; } = DeviceType.Unknown;

        // SYSTEM INFO
        public string Os { get; set; } = string.Empty;
        public string? SqlVersion { get; set; }
        public string? PosVersion { get; set; }
        public string? AgentVersion { get; set; }

        // HARDWARE INVENTORY
        public string? CpuModel { get; set; }
        public long TotalRamMB { get; set; }
        public long TotalDiskGB { get; set; }
        public string? GpuModel { get; set; }
        public string? SerialNumber { get; set; } // BIOS Serial (Win32_BIOS.SerialNumber)

        // USER & SESSION
        public string? LastLoggedInUser { get; set; }

        // ONLINE STATUS & TIMING
        public bool Online { get; set; }
        public bool ExcludeFromOfflineList { get; set; }
        public bool IsTemporarilyClosed { get; set; }
        public string? TemporaryCloseReason { get; set; }
        public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeen { get; set; }
        public DateTime? SystemBootTime { get; set; } // For uptime calculation

        // HEALTH
        public string HealthStatus { get; set; } = "Unknown";
        public int HealthScore { get; set; } = 100;

        // LIVE METRICS
        public float CurrentCpuUsagePercent { get; set; }
        public float CurrentRamUsagePercent { get; set; }
        public float CurrentDiskUsagePercent { get; set; }

        // D DRIVE METRICS
        public float? CurrentDiskDUsagePercent { get; set; }
        public long? TotalDiskDGB { get; set; }

        // VNC (Web Remote Desktop)
        public bool VncInstalled { get; set; }
        public string? VncPassword { get; set; } // Encrypted, per-device unique
        public int VncPort { get; set; } = 5900;

        // VISIBILITY — true ise non-admin kullanıcılar bu cihazı listede görmez (admin görür ve toggle eder).
        // Genel amaç: admin/team makineleri (laptop'lar, test PC'leri) operasyon ekibinden gizlemek.
        public bool HiddenForNonAdmins { get; set; }

        // NAVIGATION PROPERTY
        public List<DeviceMetric> Metrics { get; set; } = new();
    }
}
