using System;
using System.ComponentModel.DataAnnotations; // EKLENDİ!
using Orchestra.Shared.Enums;
using Orchestra.Backend.Data; // Gerekli değilse silinebilir.

namespace Orchestra.Backend.Models
{
    public class CommandResultRecord
    {
        public long Id { get; set; } // Primary Key

        public Guid CommandId { get; set; } // Komutun benzersiz ID'si
        
        [MaxLength(450)] // 🏆 DÜZELTME: Sütun uzunluğunu kesinleştirdi
        public string DeviceId { get; set; } = default!; 
        public CommandType CommandType { get; set; }
        
        // Command execution status
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty; // Konsol çıktısı
        public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;

        // Navigation Property: Komutun gönderildiği cihaz
        // 🏆 DÜZELTME: Navigasyon özelliğini nullable yaptık.
        public Device? Device { get; set; } = default!; 
    }
}