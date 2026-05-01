using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models
{
    /// <summary>
    /// Router ping latency ornegi. DeviceStatusWorker her tarama dongusunde bir satir ekler.
    /// Karasal / 4.5G (mobil) hat tespiti icin kullanilir.
    /// </summary>
    public class RouterLatencySample
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [StringLength(100)]
        public string DeviceId { get; set; } = "";

        [Required]
        public int StoreCode { get; set; }

        [Required]
        [StringLength(15)]
        public string Ip { get; set; } = "";

        /// <summary>ICMP roundtrip time (ms). Ping basarisizsa null.</summary>
        public int? RttMs { get; set; }

        /// <summary>Ping basarili mi? false = timeout/unreachable.</summary>
        public bool Success { get; set; }

        public DateTime SampledAt { get; set; } = DateTime.UtcNow;
    }
}
