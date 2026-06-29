using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models;

/// <summary>
/// Menü yetki grubu (profil). Her kullanıcı bir profile atanır; profil hangi sidebar
/// menülerini görebileceğini belirler. Admin rolü tüm profillerin üstündedir (her şeyi görür).
/// </summary>
public class MenuProfile
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(60)]
    public string Name { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Description { get; set; }

    /// <summary>Sistem profili (Teknisyen, Superuser) — silinemez, adı değiştirilemez.</summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// true  → tüm menüler taban olarak açık; sadece <see cref="HiddenMenusJson"/> içindekiler gizli.
    ///          (İleride eklenen yeni menüler de otomatik açık gelir — "Superuser" davranışı.)
    /// false → hiçbir menü açık değil; sadece <see cref="AllowedMenusJson"/> içindekiler görünür.
    ///          (Yeni menüler kapalı gelir — güvenli varsayılan, dar gruplar için.)
    /// </summary>
    public bool AllowAllByDefault { get; set; }

    /// <summary>AllowAllByDefault=false iken görünür menü path'leri (JSON dizi: ["/magazalar", ...]).</summary>
    public string AllowedMenusJson { get; set; } = "[]";

    /// <summary>AllowAllByDefault=true iken gizlenen menü path'leri (JSON dizi).</summary>
    public string HiddenMenusJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
