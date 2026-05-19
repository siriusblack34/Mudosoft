namespace Orchestra.Backend.Services;

/// <summary>
/// Yaygin Windows Event Log olaylarini Turkce aciklama ve cozum onerisi ile zenginlestirir.
/// Backend tarafinda calisir — collector verisi geldiginde veya frontend sorguladiginda uygulanir.
/// </summary>
public sealed class EventLogTranslationService
{
    private static readonly Dictionary<(string Source, long EventId), EventTranslation> Translations = new()
    {
        // ─── Disk / Storage ───
        [("Disk", 7)] = new("Disk bozuk blok veya I/O hatasi", "Disk yuzeyinde sorunlu sektor var. chkdsk /r calistirin, SMART degerlerini kontrol edin."),
        [("Disk", 11)] = new("Disk I/O hatasi tespit edildi", "Disk sagligini kontrol edin. SMART degerlerini inceleyin. Disk degisimi gerekebilir."),
        [("Disk", 15)] = new("Disk cihazi hazir degil", "Disk baglantilarini kontrol edin. USB disk ise yeniden takin."),
        [("Disk", 51)] = new("Disk uzerinde yazma yapilamadi", "Disk sagligini ve dosya sistemini kontrol edin. SMART/chkdsk yapin."),
        [("Disk", 153)] = new("Disk komutu zaman asimina ugradi", "Disk performans sorunu. SATA kablosu, kontroller veya disk arizali olabilir."),
        [("Ntfs", 55)] = new("NTFS dosya sistemi yapisinda bozukluk", "chkdsk /f komutuyla disk onarimi calistirin. Veri kaybi riski var, yedek alin."),
        [("Ntfs", 137)] = new("NTFS dosya sistemine yazma hatasi", "Disk dolu olabilir veya donanim arizasi. Disk alanini ve SMART degerlerini kontrol edin."),
        [("Ntfs", 130)] = new("NTFS dosya sistemi metadata bozulmasi", "chkdsk /f acil calistirin. Dump dosyalarini ve disk durumunu kontrol edin."),
        [("volsnap", 25)] = new("Shadow copy depolama yetersiz", "VSS depolama alanini buyutun veya gereksiz shadow copy'leri silin."),
        [("volmgr", 161)] = new("Volume manager hatasi", "Diskte fiziksel sorun olabilir. SMART ve event log diski izleyin."),

        // ─── Service Control Manager ───
        [("Service Control Manager", 7000)] = new("Servis baslatilamadi", "Servis bagimliliklarini kontrol edin. services.msc'den manuel baslatmayi deneyin."),
        [("Service Control Manager", 7001)] = new("Servis bagimlilik hatasi — bagimli servis baslatilamadi", "Bagimli servislerin durumunu kontrol edin. Sirasiyla baslatin."),
        [("Service Control Manager", 7009)] = new("Servis yanit zaman asimina ugradi (30sn / 120sn)", "Servis cok yavas basliyor. Disk performansini, timeout ayarini ve baslangic bagimliliklarini kontrol edin."),
        [("Service Control Manager", 7011)] = new("Servis kontrol istegine zamaninda yanit vermedi", "Sistem yogun olabilir. RAM ve CPU kullanimini kontrol edin. Disk hung olabilir."),
        [("Service Control Manager", 7023)] = new("Servis hata ile sonlandi", "Servis loglarini inceleyin. Servisi yeniden baslatin."),
        [("Service Control Manager", 7024)] = new("Servis hizmete ozel hata ile sonlandi", "Servisin uygulama loguna bakin. Configuration veya dependency sorunu olabilir."),
        [("Service Control Manager", 7026)] = new("Boot/sistem suruculeri yuklenemedi", "Driver eksik veya bozuk. Cihaz Yoneticisi'nde hatali surucu var mi kontrol edin."),
        [("Service Control Manager", 7031)] = new("Servis beklenmedik sekilde sonlandi — kurtarma eylemi uygulaniyor", "Servisin neden coktugunu arastirin. Bellek sizintisi olabilir."),
        [("Service Control Manager", 7034)] = new("Servis beklenmedik sekilde sonlandi (tekrarlayan)", "Servis surekli cokuyor. Uygulama loglarini ve bagimliliklari kontrol edin."),
        [("Service Control Manager", 7038)] = new("Servis logon hatasi", "Servis kimligi sifresi yanlis veya hesap kilitli. Service account'u dogrulayin."),

        // ─── SQL Server ───
        [("MSSQLSERVER", 17052)] = new("SQL Server: Hata gunlugu dosyasi doldu", "SQL Server error log dosyasini temizleyin. sp_cycle_errorlog calistirin."),
        [("MSSQLSERVER", 17204)] = new("SQL Server: dosya acilamadi", "SQL data/log dosyasina erisim sorunu. Disk doluluk ve izinleri kontrol edin."),
        [("MSSQLSERVER", 18456)] = new("SQL Server: Giris basarisiz (Authentication failure)", "Kullanici adi/sifre yanlis veya hesap kilitli. SQL Server loglarini kontrol edin."),
        [("MSSQL$SQLEXPRESS", 18456)] = new("SQL Express: Giris basarisiz", "Kullanici adi/sifre yanlis veya hesap kilitli."),
        [("MSSQLSERVER", 9001)] = new("SQL Server: Log dosyasi kullanilamiyor", "Database log dosyasi bozulmus olabilir. Yedekten geri yukleme gerekebilir."),
        [("MSSQLSERVER", 823)] = new("SQL Server: G/C hatasi (823)", "Disk seviyesinde okuma/yazma hatasi. Diski acil olarak kontrol edin."),
        [("MSSQLSERVER", 824)] = new("SQL Server: Mantiksal G/C hatasi (824)", "Sayfa bozulmasi tespit edildi. DBCC CHECKDB calistirin."),

        // ─── Windows Update ───
        [("WindowsUpdateClient", 20)] = new("Windows Update kurulumu basarisiz", "Windows Update sorun gidericisini calistirin. SoftwareDistribution klasorunu temizleyin."),
        [("WindowsUpdateClient", 25)] = new("Windows Update indirme basarisiz", "Internet baglantisini kontrol edin. BITS servisini yeniden baslatin."),
        [("WindowsUpdateClient", 31)] = new("Windows Update servisi durdurulamadi", "Servis hung. Bilgisayari yeniden baslatip tekrar deneyin."),

        // ─── Application Error / Hang ───
        [("Application Error", 1000)] = new("Uygulama coktu (crash)", "Uygulama loglarini inceleyin. Uygulamayi guncelleyin veya yeniden kurun."),
        [("Application Hang", 1002)] = new("Uygulama yanit vermiyor (hang/freeze)", "RAM ve CPU kullanimini kontrol edin. Uygulama guncellemesi gerekebilir."),
        [(".NET Runtime", 1026)] = new(".NET uygulamasinda yakalanmamis exception", "Uygulama loglarini inceleyin. Stack trace'i analiz edin."),
        [("Application Error", 1001)] = new("Windows Error Reporting raporu olusturuldu", "Hangi uygulamanin coktugune bakin. Crash dump analiz edin."),

        // ─── Kernel / Power / System ───
        [("Microsoft-Windows-Kernel-Power", 41)] = new("Sistem beklenmedik sekilde kapandi (guc kesintisi, BSOD veya hard reset)", "UPS durumunu kontrol edin. Guc kaynagi sorunlu olabilir. Dump dosyalarini inceleyin."),
        [("Microsoft-Windows-Kernel-Power", 142)] = new("Sistem watchdog tarafindan otomatik baslatildi", "Sistem hang sonrasi watchdog devreye girdi. Surucu/donanim sorunu olabilir."),
        [("Microsoft-Windows-Kernel-General", 12)] = new("Sistem baslangici", "Bilgilendirme — sistem acildi."),
        [("Microsoft-Windows-Kernel-General", 13)] = new("Sistem kapanisi", "Bilgilendirme — sistem kapandi."),
        [("Microsoft-Windows-Kernel-Boot", 27)] = new("Boot tipi (cold/warm) bilgisi", "Bilgilendirme. Eger sik cold boot varsa beklenmedik kapanma sayilir."),
        [("Microsoft-Windows-WHEA-Logger", 1)] = new("Donanim hatasi (WHEA)", "CPU, RAM veya PCI cihazindan donanim hatasi. Detayli loga ve donanim teshisine bakin."),
        [("Microsoft-Windows-WHEA-Logger", 18)] = new("WHEA: Onarilan donanim hatasi", "Donanim seviyesinde hata duzeltildi ama kotulesme sinyali — RAM/CPU/disk degisimi planlayin."),
        [("Microsoft-Windows-WHEA-Logger", 19)] = new("WHEA: PCI Express dali kotulesti", "PCI cihazi (NIC, GPU, NVMe) saglikli degil. Hardware diagnostic gerekli."),
        [("BugCheck", 1001)] = new("Mavi ekran (BSOD) olustu", "C:\\Windows\\Minidump dump dosyasini analiz edin. Surucu guncellemeleri kontrol edin. RAM testi yapin."),
        [("EventLog", 6005)] = new("Event Log servisi baslatildi (sistem acildi)", "Bilgilendirme."),
        [("EventLog", 6006)] = new("Event Log servisi durduruldu (temiz kapanis)", "Bilgilendirme."),
        [("EventLog", 6008)] = new("Sistem onceki kapanista beklenmedik sekilde kapandi", "Guc kesintisi veya donanim sorunu. UPS ve guc kaynagini kontrol edin."),
        [("EventLog", 6013)] = new("Sistem calisma suresi (uptime) bilgisi", "Bilgilendirme — sistem ne kadar suredir acik."),
        [("USER32", 1074)] = new("Sistem kullanici/process tarafindan kapatildi", "Kim tarafindan ne icin kapatildi? Update/restart komutu olabilir."),
        [("Microsoft-Windows-Eventlog", 104)] = new("Event log temizlendi", "Birisi event log'u temizledi. Yetkili bir aksiyon mu kontrol edin."),

        // ─── Network ───
        [("Tcpip", 4199)] = new("IP adresi cakismasi tespit edildi", "Agda ayni IP'yi kullanan baska cihaz var. DHCP ayarlarini kontrol edin."),
        [("Tcpip", 4227)] = new("TCP/IP geri donus paketi alamadi", "Ag baglantisinda kopukluk. Switch/kablo kontrol."),
        [("DNS Client Events", 1014)] = new("DNS cozumleme zaman asimi", "DNS sunucu ayarlarini kontrol edin. Ag baglantisini test edin."),
        [("Dhcp-Client", 1003)] = new("DHCP'den IP alinamadi", "DHCP sunucu erisilebilir mi? Statik IP'ye gecmeyi degerlendirin."),
        [("Dhcp-Client", 50036)] = new("DHCP istemcisinden kira yenileme basarisiz", "DHCP sunucu kapatilmis veya scope dolmus olabilir."),
        [("Microsoft-Windows-NlaSvc", 4001)] = new("Network Location Awareness — ag profili degisti", "Bilgilendirme. Sik degisiyorsa NIC takilip cikiyor olabilir."),
        [("e1iexpress", 27)] = new("Ag adaptoru baglantisi kesildi", "Ethernet kablosunu kontrol edin. Switch/router durumunu dogrulayin."),
        [("e1iexpress", 32)] = new("Ag adaptoru linki dustu", "Kablo veya switch port problemi."),
        [("nvlddmkm", 13)] = new("NVIDIA surucu hatasi (TDR)", "GPU surucusu cevap vermedi ve resetlendi. GPU surucusunu guncelleyin."),
        [("Display", 4101)] = new("Display surucusu cevap vermedi ve geri yuklendi (TDR)", "GPU surucusu hung. Surucu guncelleme/rollback yapin."),

        // ─── USB / Donanim ───
        [("Microsoft-Windows-Kernel-PnP", 219)] = new("Driver yuklenemedi", "Cihaz Yoneticisi'nde sarisi simgeli cihaz var mi? Driver eksik."),
        [("disk", 27)] = new("Disk firmware bilgisi", "Bilgilendirme."),
        [("USBHUB", 22)] = new("USB cihazinda enumerasyon hatasi", "USB cihazini cikarip yeniden takin. Kablo/port kontrol."),

        // ─── Print ───
        [("PrintService", 372)] = new("Yazici hatasi", "Yazici durumunu kontrol edin, spool servisini yeniden baslatin."),

        // ─── Group Policy / WinLogon ───
        [("Microsoft-Windows-GroupPolicy", 1129)] = new("Group Policy uygulanamadi (network/DC erisilemedi)", "DC ile baglanti kopuk olabilir. DNS ve domain baglantisini kontrol edin."),
        [("Microsoft-Windows-User Profiles Service", 1530)] = new("Kullanici profil dosyasinda kilit hatasi", "Profil servis problemi. Cihazi yeniden baslatin."),

        // ─── Time ───
        [("Microsoft-Windows-Time-Service", 50)] = new("Sistem saati senkronize edilemedi", "NTP sunucu erisimi var mi? AD domain join saglikli mi?"),
    };

    /// <summary>
    /// Verilen EventLog girdisine Turkce aciklama ve cozum onerisi ekler.
    /// </summary>
    public (string? TranslatedMessage, string? SuggestedAction) Translate(string source, long eventId)
    {
        if (Translations.TryGetValue((source, eventId), out var t))
            return (t.Description, t.Action);

        return (null, null);
    }

    private record EventTranslation(string Description, string Action);
}
