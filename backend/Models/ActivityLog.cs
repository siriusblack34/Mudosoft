using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models
{
    /// <summary>
    /// Genel islem audit log'u — komut tabanli olmayan tum islemler buraya yazilir
    /// (cleanup, envanter import, magaza eslestirme, ayar degisiklikleri vb.)
    /// </summary>
    public class ActivityLog
    {
        public int Id { get; set; }

        [StringLength(100)]
        public string? Username { get; set; }

        [Required]
        [StringLength(40)]
        public string Category { get; set; } = string.Empty; // Cleanup, Inventory, RemoteInstall, Settings, ...

        [Required]
        [StringLength(80)]
        public string Action { get; set; } = string.Empty;   // CleanAll, Import, UpdateMapping, ...

        [StringLength(200)]
        public string? Target { get; set; }                  // device id, magaza kodu, dosya adi vb.

        public string? Details { get; set; }                 // serbest metin / JSON

        public bool Success { get; set; } = true;

        [StringLength(2000)]
        public string? ErrorMessage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
