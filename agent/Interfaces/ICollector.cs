namespace Mudosoft.Agent.Interfaces;

/// <summary>
/// Periyodik veri toplayan tüm collector servislerin ortak arayüzü.
/// Her collector bağımsız çalışır, biri çökse diğerlerini etkilemez.
/// </summary>
public interface ICollector
{
    /// <summary>Collector adı (loglama ve raporlama için)</summary>
    string Name { get; }

    /// <summary>Toplama aralığı</summary>
    TimeSpan Interval { get; }

    /// <summary>Bu collector aktif mi? (appsettings'ten okunur)</summary>
    bool Enabled { get; }

    /// <summary>Veri topla ve sonuç döndür</summary>
    Task<CollectorResult> CollectAsync(CancellationToken ct);
}

/// <summary>
/// Bir collector'ın tek bir toplama döngüsünün sonucu.
/// </summary>
public class CollectorResult
{
    public string CollectorName { get; set; } = "";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Severity { get; set; } = "Info"; // Info, Warning, Critical
    public string JsonData { get; set; } = "{}";
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
