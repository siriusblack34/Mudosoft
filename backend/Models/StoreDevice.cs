using System;
using System.ComponentModel.DataAnnotations;

namespace MudoSoft.Backend.Models
{
    public class StoreDevice
    {
        // DeviceId artık string çünkü frontend string gönderiyor
        [Key]
        [Required]
        [StringLength(100)]
        public string DeviceId { get; set; } = string.Empty;


        // Mağaza kodu
        [Required]
        public int StoreCode { get; set; }


        // Mağaza adı
        [Required]
        [StringLength(100)]
        public string StoreName { get; set; } = string.Empty;


        // Cihaz tipi: PC, KK1, KK2, KK3
        [Required]
        [StringLength(10)]
        public string DeviceType { get; set; } = string.Empty;


        // Kullanıcıya görünen ad (POS1, PC, KK2 vs.)
        [Required]
        [StringLength(50)]
        public string DeviceName { get; set; } = string.Empty;


        // IP adresi
        [Required]
        [StringLength(15)]
        public string CalculatedIpAddress { get; set; } = string.Empty;


        // DB bağlantı dizesi
        [Required]
        [StringLength(256)]
        public string DbConnectionString { get; set; } = string.Empty;


        public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSyncDate { get; set; } = DateTimeOffset.UtcNow;
    }
}
