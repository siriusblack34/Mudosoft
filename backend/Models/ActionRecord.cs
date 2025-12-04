// backend/Models/ActionRecord.cs

using System;
using System.ComponentModel.DataAnnotations;

namespace MudoSoft.Backend.Models
{
    public class ActionRecord
    {
        [Key]
        public Guid RecordId { get; set; } = Guid.NewGuid();
        
        // Cihazı Guid olarak tutuyoruz.
        public Guid DeviceId { get; set; } 
        
        // Fix CS0117: ActionType tanımı eklendi.
        [Required]
        [StringLength(50)]
        public required string ActionType { get; set; } 
        
        // Fix CS0117: Payload tanımı eklendi.
        public string? Payload { get; set; } 
        
        // Fix CS0117: ExecutionDate tanımı eklendi.
        public DateTimeOffset ExecutionDate { get; set; } = DateTimeOffset.UtcNow;
        
        [Required]
        [StringLength(20)]
        public required string Status { get; set; } 
        
        // Fix CS0117: Result tanımı eklendi.
        public string? Result { get; set; }
    }
}