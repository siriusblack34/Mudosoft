using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models
{
    /// <summary>
    /// SDP "User" alanindaki magaza adini Orchestra StoreCode'una eslestirmek icin.
    /// Ornek: "212 Outlet" -> 212
    /// </summary>
    public class StoreNameMapping
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string RawName { get; set; } = string.Empty;

        public int? StoreCode { get; set; }

        public bool AutoMatched { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
