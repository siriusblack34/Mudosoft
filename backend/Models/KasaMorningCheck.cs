using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models;

public class KasaMorningCheck
{
    [Key]
    public int Id { get; set; }

    [MaxLength(100)]
    public string StoreDeviceId { get; set; } = "";

    public int StoreCode { get; set; }

    [MaxLength(100)]
    public string StoreName { get; set; } = "";

    [MaxLength(20)]
    public string DeviceType { get; set; } = "";

    [MaxLength(15)]
    public string IpAddress { get; set; } = "";

    /// <summary>Kontrol zamanı (UTC)</summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>UNC paylaşımına erişilebildi mi?</summary>
    public bool IsUncReachable { get; set; }

    /// <summary>Bugünün GeniusPOS log dosyası bulundu mu?</summary>
    public bool IsGeniusPosLogFound { get; set; }

    /// <summary>Kasa sağlıklı mı? (UNC erişilebilir + log mevcut)</summary>
    public bool IsHealthy { get; set; }

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }
}
