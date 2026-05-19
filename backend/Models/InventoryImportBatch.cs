using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models
{
    public class InventoryImportBatch
    {
        public int Id { get; set; }

        [StringLength(255)]
        public string? FileName { get; set; }

        public long FileSizeBytes { get; set; }

        [StringLength(100)]
        public string? ImportedBy { get; set; }

        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

        public int TotalRows { get; set; }
        public int InsertedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int UnmatchedStoreCount { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Completed"; // Completed | Failed | Partial

        public string? ErrorMessage { get; set; }
    }
}
