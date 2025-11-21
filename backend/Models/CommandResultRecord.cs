using System;
using Mudosoft.Shared.Enums;
using MudoSoft.Backend.Data;

namespace MudoSoft.Backend.Models
{
    public class CommandResultRecord
    {
        public long Id { get; set; } // Primary Key

        public Guid CommandId { get; set; } // Komutun benzersiz ID'si
        public string DeviceId { get; set; } = default!; 
        public CommandType CommandType { get; set; }
        
        // Command execution status
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty; // Konsol çıktısı
        public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;

        // Navigation Property: Komutun gönderildiği cihaz
        public Device Device { get; set; } = default!; 
    }
}