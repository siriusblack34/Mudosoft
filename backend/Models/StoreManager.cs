using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Orchestra.Backend.Models
{
    public class StoreManager
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; } // Internal Auto-Incrementing ID

        public int StoreCode { get; set; } // Kod

        [Required]
        [MaxLength(200)]
        public string StoreName { get; set; } = string.Empty; // Mağaza Adı

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty; // Ad Soyad

        [MaxLength(20)]
        public string Phone { get; set; } = string.Empty; // Tel
    }
}
