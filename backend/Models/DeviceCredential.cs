using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models;

/// <summary>
/// 🔒 Faz 2 — cihaz-başına kimlik + komut sıra sayacı (K-5/K-2 temeli).
/// Enrollment'ta cihazın public key'i kaydedilir; LastCommandSeq her imzalı komutta artar (replay koruması).
/// Stage 0'da satır, komut imzalanırken lazily oluşturulur (PublicKey null = henüz enroll olmamış).
/// </summary>
public class DeviceCredential
{
    [Key]
    public string DeviceId { get; set; } = default!;

    /// <summary>Cihazın RSA public key'i (XML). Enroll edilene kadar null.</summary>
    public string? PublicKey { get; set; }

    public DateTime? EnrolledAtUtc { get; set; }

    /// <summary>Per-device monotonik komut sıra numarası — agent replay kontrolü için.</summary>
    public long LastCommandSeq { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastSeenAtUtc { get; set; }
}
