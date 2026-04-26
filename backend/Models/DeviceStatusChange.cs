using System.ComponentModel.DataAnnotations;

namespace MudoSoft.Backend.Models
{
    /// <summary>
    /// Cihaz online/offline durum gecis kaydi.
    /// Her status degisikligi bir satir olusturur.
    /// </summary>
    public class DeviceStatusChange
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [StringLength(100)]
        public string DeviceId { get; set; } = "";

        [Required]
        public int StoreCode { get; set; }

        [Required]
        [StringLength(20)]
        public string DeviceType { get; set; } = ""; // ROUTER, PC, KASA-1, KASA-2, ...

        /// <summary>true = cihaz online'a gecti, false = offline'a dustu</summary>
        public bool IsOnline { get; set; }

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }
}
