using System.ComponentModel.DataAnnotations;

namespace MudoSoft.Backend.Models;

/// <summary>
/// Collector verilerini saklayan ana tablo.
/// Her satır bir collector'ın tek bir çalışma sonucunu temsil eder.
/// </summary>
public class CollectorReport
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(450)]
    public string DeviceId { get; set; } = "";

    [Required, MaxLength(50)]
    public string CollectorName { get; set; } = "";

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(20)]
    public string Severity { get; set; } = "Info";

    /// <summary>JSON formatında collector verisi</summary>
    public string JsonData { get; set; } = "{}";

    public bool Success { get; set; } = true;

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }
}
