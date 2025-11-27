using System;
using System.ComponentModel.DataAnnotations; // EKLENDİ!

namespace MudoSoft.Backend.Models
{
    public class DeviceMetric
    {
        public long Id { get; set; }

        // Foreign Key sütunu
        [MaxLength(450)] // 🏆 DÜZELTME: Sütun uzunluğunu kesinleştirdi
        public string DeviceId { get; set; } = default!;
        
        // 🏆 DÜZELTME: Navigasyon özelliğini nullable yaptık. 
        // Bu, EF Core'un 'DeviceId1' adında yeni bir gölge sütun oluşturmasını engeller.
        public Device? Device { get; set; } = default!; 

        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public int CpuUsagePercent { get; set; }
        public int RamUsagePercent { get; set; }
        public int DiskUsagePercent { get; set; }

        // Future expansion:
        public double? CpuTemperature { get; set; }
        public double? DiskFreeGb { get; set; }
    }
}