using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models
{
    public class InventoryAsset
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string AssetName { get; set; } = string.Empty;

        public int? StoreCode { get; set; }

        [StringLength(200)]
        public string? StoreNameRaw { get; set; }

        [StringLength(100)]
        public string? Department { get; set; }

        [StringLength(150)]
        public string? ProductType { get; set; }

        [StringLength(200)]
        public string? Product { get; set; }

        [StringLength(100)]
        public string? ProductCode { get; set; }

        [StringLength(150)]
        public string? OrgSerialNumber { get; set; }

        [StringLength(100)]
        public string? ComputerName { get; set; }

        [StringLength(50)]
        public string? MacAddress { get; set; }

        [StringLength(500)]
        public string? AssetTag { get; set; }

        public DateTime? AcquisitionDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        [StringLength(100)]
        public string? YazarkasaSicilNo { get; set; }

        [StringLength(100)]
        public string? BaseSeriNo { get; set; }

        [StringLength(100)]
        public string? PrinterSeriNo { get; set; }

        [StringLength(100)]
        public string? IkinciMonitorSeriNo { get; set; }

        [StringLength(50)]
        public string? IpAddress { get; set; }

        [StringLength(50)]
        public string? AssetState { get; set; }

        [StringLength(50)]
        public string? FizikselDurum { get; set; }

        public decimal? PurchaseCost { get; set; }

        [StringLength(100)]
        public string? FaturaNo { get; set; }

        [StringLength(100)]
        public string? TalepNo { get; set; }

        public string? ExtraJson { get; set; }

        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

        public int? ImportBatchId { get; set; }
        public InventoryImportBatch? ImportBatch { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
