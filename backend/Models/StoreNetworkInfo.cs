using System.ComponentModel.DataAnnotations;

namespace MudoSoft.Backend.Models
{
    /// <summary>
    /// Magazanin karasal hat (ISP) referans bilgisi.
    /// Manuel olarak bakilan / tanimlanmis taahhut degerleri.
    /// </summary>
    public class StoreNetworkInfo
    {
        [Key]
        public int StoreCode { get; set; }

        /// <summary>Taahhut edilen karasal hat hizi (Mbps). Turkcell/ISP kaynagi.</summary>
        public int TerrestrialMbps { get; set; }

        /// <summary>Hat tipi: Fiber, Bakir (VDSL), vs. Opsiyonel.</summary>
        [StringLength(30)]
        public string? LineType { get; set; }

        /// <summary>Manuel not (ornegin: 'yedekli 4.5G', 'statik IP').</summary>
        [StringLength(200)]
        public string? Notes { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
