using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models
{
    public class ScheduledTask
    {
        [Key]
        public int Id { get; set; }

        public string TaskType { get; set; } = "InboxCleanup"; // "InboxCleanup", "StockCleanup" etc.

        // "OneTime", "Daily"
        public string Frequency { get; set; } = "OneTime"; 

        // Günlük görevler için (Örn: 12:00:00)
        public TimeSpan? TargetTime { get; set; }

        // Tek seferlik görevler için (Tarih + Saat)
        public DateTime? TargetDate { get; set; }

        public DateTime NextRunTime { get; set; }

        public DateTime? LastRunTime { get; set; }
        public string? LastResult { get; set; }
        
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
