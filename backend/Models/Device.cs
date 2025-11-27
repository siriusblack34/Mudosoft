using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace MudoSoft.Backend.Models
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
        public DeviceType Type { get; set; } = DeviceType.Unknown; // ✅ Type bilgisi

        // SYSTEM INFO
        public string Os { get; set; } = string.Empty;
        public string? SqlVersion { get; set; }
        public string? PosVersion { get; set; }
        public string? AgentVersion { get; set; }

        // ONLINE STATUS
        public bool Online { get; set; } // ✅ Status (online/offline) bilgisi
        public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeen { get; set; }

        // HEALTH
        public string HealthStatus { get; set; } = "Unknown";
        public int HealthScore { get; set; } = 100;
        
        // ✅ GÜNCELLEME: Canlı metrik sütunları aktifleştirildi (CPU/RAM için)
        public float CurrentCpuUsagePercent { get; set; } // ✅ CPU bilgisi
        public float CurrentRamUsagePercent { get; set; } // ✅ RAM bilgisi
        public float CurrentDiskUsagePercent { get; set; }

        // NAVIGATION PROPERTY
        public List<DeviceMetric> Metrics { get; set; } = new();
    }
}