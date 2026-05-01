namespace Orchestra.Backend.Services;

/// <summary>
/// Yaygın Windows Event Log olaylarını Türkçe açıklama ve çözüm önerisi ile zenginleştirir.
/// Backend tarafında çalışır - collector verisi geldiğinde veya frontend sorguladığında uygulanır.
/// </summary>
public sealed class EventLogTranslationService
{
    private static readonly Dictionary<(string Source, long EventId), EventTranslation> Translations = new()
    {
        // ─── Disk / Storage ───
        [("Disk", 11)] = new(
            "Disk I/O hatası tespit edildi",
            "Disk sağlığını kontrol edin. SMART değerlerini inceleyin. Disk değişimi gerekebilir."),
        [("Disk", 15)] = new(
            "Disk cihazı hazır değil",
            "Disk bağlantılarını kontrol edin. USB disk ise yeniden takın."),
        [("Ntfs", 55)] = new(
            "NTFS dosya sistemi yapısında bozukluk",
            "chkdsk /f komutuyla disk onarımı çalıştırın. Veri kaybı riski var, yedek alın."),
        [("Ntfs", 137)] = new(
            "NTFS dosya sistemine yazma hatası",
            "Disk dolu olabilir veya donanım arızası. Disk alanını ve SMART değerlerini kontrol edin."),

        // ─── Service Control Manager ───
        [("Service Control Manager", 7000)] = new(
            "Servis başlatılamadı",
            "Servis bağımlılıklarını kontrol edin. services.msc'den manuel başlatmayı deneyin."),
        [("Service Control Manager", 7001)] = new(
            "Servis bağımlılık hatası - bağımlı servis başlatılamadı",
            "Bağımlı servislerin durumunu kontrol edin. Sırasıyla başlatın."),
        [("Service Control Manager", 7009)] = new(
            "Servis yanıt zaman aşımına uğradı",
            "Servis çok yavaş başlıyor. Timeout değerini artırın veya disk performansını kontrol edin."),
        [("Service Control Manager", 7011)] = new(
            "Servis kontrol isteğine zamanında yanıt vermedi",
            "Sistem yoğun olabilir. RAM ve CPU kullanımını kontrol edin."),
        [("Service Control Manager", 7023)] = new(
            "Servis hata ile sonlandı",
            "Servis loglarını inceleyin. Servisi yeniden başlatın."),
        [("Service Control Manager", 7031)] = new(
            "Servis beklenmedik şekilde sonlandı - kurtarma eylemi uygulanıyor",
            "Servisin neden çöktüğünü araştırın. Bellek sızıntısı olabilir."),
        [("Service Control Manager", 7034)] = new(
            "Servis beklenmedik şekilde sonlandı (tekrarlayan)",
            "Servis sürekli çöküyor. Uygulama loglarını ve bağımlılıkları kontrol edin."),

        // ─── SQL Server ───
        [("MSSQLSERVER", 17052)] = new(
            "SQL Server: Hata günlüğü dosyası doldu veya erişilemez",
            "SQL Server error log dosyasını temizleyin. sp_cycle_errorlog çalıştırın."),
        [("MSSQLSERVER", 18456)] = new(
            "SQL Server: Giriş başarısız (Authentication failure)",
            "Kullanıcı adı/şifre yanlış veya hesap kilitli. SQL Server loglarını kontrol edin."),
        [("MSSQL$SQLEXPRESS", 18456)] = new(
            "SQL Express: Giriş başarısız (Authentication failure)",
            "Kullanıcı adı/şifre yanlış veya hesap kilitli. SQL Server loglarını kontrol edin."),

        // ─── Windows Update ───
        [("WindowsUpdateClient", 20)] = new(
            "Windows Update kurulumu başarısız oldu",
            "Windows Update sorun gidericisini çalıştırın. SoftwareDistribution klasörünü temizleyin."),
        [("WindowsUpdateClient", 25)] = new(
            "Windows Update indirme başarısız oldu",
            "İnternet bağlantısını kontrol edin. BITS servisini yeniden başlatın."),

        // ─── Application Error ───
        [("Application Error", 1000)] = new(
            "Uygulama çöktü (crash)",
            "Uygulama loglarını inceleyin. Uygulamayı güncelleyin veya yeniden kurun."),
        [("Application Hang", 1002)] = new(
            "Uygulama yanıt vermiyor (hang/freeze)",
            "RAM ve CPU kullanımını kontrol edin. Uygulama güncellemesi gerekebilir."),

        // ─── Kernel / System ───
        [("Microsoft-Windows-Kernel-Power", 41)] = new(
            "Sistem beklenmedik şekilde kapandı (güç kesintisi veya mavi ekran)",
            "UPS durumunu kontrol edin. Güç kaynağı sorunlu olabilir. Dump dosyalarını inceleyin."),
        [("BugCheck", 1001)] = new(
            "Mavi ekran (BSOD) oluştu",
            "Dump dosyasını analiz edin. Sürücü güncellemelerini kontrol edin. RAM testi yapın."),
        [("EventLog", 6008)] = new(
            "Sistem önceki kapanışta beklenmedik şekilde kapandı",
            "Güç kesintisi veya donanım sorunu. UPS ve güç kaynağını kontrol edin."),
        [("EventLog", 6013)] = new(
            "Sistem çalışma süresi (uptime) bilgisi",
            "Bilgilendirme amaçlı. Sistem ne kadar süredir açık olduğunu gösterir."),

        // ─── Network ───
        [("Tcpip", 4199)] = new(
            "IP adresi çakışması tespit edildi",
            "Ağda aynı IP'yi kullanan başka bir cihaz var. DHCP ayarlarını kontrol edin."),
        [("DNS Client Events", 1014)] = new(
            "DNS çözümleme zaman aşımı",
            "DNS sunucu ayarlarını kontrol edin. Ağ bağlantısını test edin."),
        [("e1iexpress", 27)] = new(
            "Ağ adaptörü bağlantısı kesildi",
            "Ethernet kablosunu kontrol edin. Switch/router durumunu doğrulayın."),
    };

    /// <summary>
    /// Verilen EventLog girdisine Türkçe açıklama ve çözüm önerisi ekler.
    /// </summary>
    public (string? TranslatedMessage, string? SuggestedAction) Translate(string source, long eventId)
    {
        if (Translations.TryGetValue((source, eventId), out var t))
            return (t.Description, t.Action);

        return (null, null);
    }

    private record EventTranslation(string Description, string Action);
}
