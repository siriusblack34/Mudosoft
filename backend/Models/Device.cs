using System;
using System.ComponentModel.DataAnnotations; // EKLENDİ!
using System.Collections.Generic; // List<T> için eklendi

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
        [Key] // Anahtar özniteliği eklendi
        [MaxLength(450)] // 🏆 DÜZELTME: Sütun uzunluğunu kesinleştirdi
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

        // ONLINE STATUS
        public bool Online { get; set; }
        public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeen { get; set; }

        // HEALTH
        public string HealthStatus { get; set; } = "Unknown"; 
        public int HealthScore { get; set; } = 100;
        
        // ❌ HATA ÇÖZÜMÜ İÇİN GEÇİCİ ÇÖZÜM: Migration'dan sonra silinecek sütunlar (şimdilik yoruma alındı)
        // public float CurrentCpuUsagePercent { get; set; }
        // public float CurrentRamUsagePercent { get; set; }
        // public float CurrentDiskUsagePercent { get; set; }

        // NAVIGATION PROPERTY
        public List<DeviceMetric> Metrics { get; set; } = new();
    }
}