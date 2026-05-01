using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models
{
    public class StoreOfflineLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int StoreCode { get; set; }

        [Required]
        [StringLength(100)]
        public string StoreName { get; set; } = "";

        /// <summary>Kaç kasa offline olarak tespit edildi</summary>
        public int OfflineKasaCount { get; set; }

        /// <summary>Mağazanın tüm kasaları offline olduğu an</summary>
        public DateTime OfflineAt { get; set; }

        /// <summary>En az bir kasa tekrar online olduğu an (null = hâlâ offline)</summary>
        public DateTime? OnlineAt { get; set; }

        /// <summary>Offline kalma süresi dakika cinsinden</summary>
        public int? DurationMinutes { get; set; }
    }
}
