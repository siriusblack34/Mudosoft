using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Shared.Dtos;

namespace Orchestra.Backend.Services;

public sealed class EventLogAnalysisService
{
    private readonly OrchestraDbContext _db;
    private readonly EventLogTranslationService _translator;

    public EventLogAnalysisService(OrchestraDbContext db, EventLogTranslationService translator)
    {
        _db = db;
        _translator = translator;
    }

    public async Task<EventLogAnalysisResultDto> AnalyzeAsync(string deviceId, int hours = 24, int limit = 200)
    {
        var clampedHours = Math.Clamp(hours, 1, 24 * 30);
        var sinceUtc = DateTime.UtcNow.AddHours(-clampedHours);

        var rawReports = await _db.CollectorReports
            .Where(r => r.DeviceId == deviceId && r.CollectorName == "EventLog")
            .OrderByDescending(r => r.TimestampUtc)
            .Take(120)
            .Select(r => new { r.JsonData, r.TimestampUtc })
            .ToListAsync();

        var latestEventLogReport = rawReports.FirstOrDefault()?.TimestampUtc;

        var entries = new List<EventLogEntryDto>();
        foreach (var report in rawReports)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<EventLogEntryDto>>(report.JsonData,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed != null) entries.AddRange(parsed);
            }
            catch { }
        }

        var normalizedEntries = entries
            .Where(e => e.TimeGenerated >= sinceUtc)
            .GroupBy(e => $"{e.TimeGenerated:O}|{e.Source}|{e.EventId}|{e.Level}")
            .Select(g =>
            {
                var item = g.First();
                var (translated, action) = _translator.Translate(item.Source, item.EventId);
                item.TranslatedMessage = translated;
                item.SuggestedAction = action;
                return item;
            })
            .OrderByDescending(e => e.TimeGenerated)
            .ToList();

        var unexpectedShutdowns = normalizedEntries.Where(IsUnexpectedShutdown).ToList();
        var bugChecks = normalizedEntries.Where(e => e.Source == "BugCheck" && e.EventId == 1001).ToList();
        var diskErrors = normalizedEntries.Where(IsDiskRelated).ToList();
        var whea = normalizedEntries.Where(e => e.Source == "Microsoft-Windows-WHEA-Logger").ToList();
        var tdr = normalizedEntries.Where(e =>
            (e.Source == "Display" && e.EventId == 4101) ||
            (e.Source == "nvlddmkm" && e.EventId == 13)).ToList();
        var serviceCrashes = normalizedEntries.Where(IsServiceCrash).ToList();
        var appErrors = normalizedEntries.Where(e =>
            (e.Source == "Application Error" && e.EventId == 1000) ||
            (e.Source == "Application Hang" && e.EventId == 1002) ||
            (e.Source == ".NET Runtime" && e.EventId == 1026)).ToList();
        var updateFailures = normalizedEntries.Where(e =>
            e.Source == "WindowsUpdateClient" && (e.EventId == 20 || e.EventId == 25 || e.EventId == 31)).ToList();
        var networkErrors = normalizedEntries.Where(e =>
            (e.Source == "Tcpip" && (e.EventId == 4199 || e.EventId == 4227)) ||
            (e.Source == "DNS Client Events" && e.EventId == 1014) ||
            (e.Source == "Dhcp-Client") ||
            (e.Source == "e1iexpress" && (e.EventId == 27 || e.EventId == 32))).ToList();
        var userInitiatedShutdowns = normalizedEntries.Where(e => e.Source == "USER32" && e.EventId == 1074).ToList();

        // Boot/shutdown timeline — kullanici amac olarak "neden ka bilgisayar kapanip aciliyor"u net gormeli
        var bootEvents = entries
            .Where(e => e.TimeGenerated >= sinceUtc.AddHours(-2))
            .Where(e =>
                (e.Source == "EventLog" && (e.EventId == 6005 || e.EventId == 6006 || e.EventId == 6008 || e.EventId == 6013)) ||
                (e.Source == "Microsoft-Windows-Kernel-Power" && e.EventId == 41) ||
                (e.Source == "Microsoft-Windows-Kernel-General" && (e.EventId == 12 || e.EventId == 13)) ||
                (e.Source == "USER32" && e.EventId == 1074))
            .OrderBy(e => e.TimeGenerated)
            .GroupBy(e => $"{e.TimeGenerated:O}|{e.Source}|{e.EventId}")
            .Select(g => g.First())
            .ToList();

        var bootShutdownTimeline = bootEvents.Select(e => new BootShutdownEventDto
        {
            TimeGenerated = e.TimeGenerated,
            Type = ClassifyBootEvent(e),
            Source = e.Source,
            EventId = e.EventId,
            Detail = _translator.Translate(e.Source, e.EventId).TranslatedMessage ?? TrimMessage(e.Message)
        }).ToList();

        // Yan veriler: disk, sicaklik, uptime collector'lari
        var latestDiskHealth = await GetLatestJsonAsync<List<DiskHealthDto>>(deviceId, "DiskHealth");
        var latestTemps = await GetLatestJsonAsync<List<TemperatureReadingDto>>(deviceId, "Temperature");
        var latestUptime = await GetLatestJsonAsync<UptimeReportDto>(deviceId, "UptimeReport");

        var criticalTempSensors = (latestTemps ?? new())
            .Where(t => string.Equals(t.Status, "Critical", StringComparison.OrdinalIgnoreCase) || t.TemperatureCelsius >= 85)
            .ToList();
        var degradedDisks = (latestDiskHealth ?? new())
            .Where(d => d.UsedPercent >= 95 ||
                        string.Equals(d.SmartStatus, "PredFail", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(d.SmartStatus, "Degraded", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // ────────────── Temporal correlation: kapanmadan onceki 30 dk'da ne oldu? ──────────────
        var shutdownChains = new List<ShutdownChainDto>();
        foreach (var shutdown in unexpectedShutdowns.Take(5))
        {
            var windowStart = shutdown.TimeGenerated.AddMinutes(-30);
            var preceding = normalizedEntries
                .Where(e => e.TimeGenerated < shutdown.TimeGenerated && e.TimeGenerated >= windowStart)
                .Where(e => e != shutdown)
                .Where(e => e.Level is "Critical" or "Error" or "Warning")
                .OrderByDescending(e => e.TimeGenerated)
                .Take(8)
                .Select(e => new EventLogTimelineItemDto
                {
                    TimeGenerated = e.TimeGenerated,
                    Source = e.Source,
                    EventId = e.EventId,
                    Level = e.Level,
                    Summary = e.TranslatedMessage ?? TrimMessage(e.Message),
                    RawMessage = TrimMessage(e.Message)
                })
                .ToList();

            shutdownChains.Add(new ShutdownChainDto
            {
                ShutdownAt = shutdown.TimeGenerated,
                ShutdownSource = shutdown.Source,
                ShutdownEventId = shutdown.EventId,
                PrecedingEvents = preceding
            });
        }

        // ────────────── Hipotezler ──────────────
        var hypotheses = new List<EventLogHypothesisDto>();

        if (bugChecks.Count > 0)
        {
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = "Yazilimsal / Surucu (BSOD)",
                Confidence = "Yuksek",
                Score = 95,
                Title = $"Son {clampedHours}sa icinde {bugChecks.Count} kez mavi ekran (BSOD) yasandi",
                Why = "BugCheck 1001 olayi isletim sisteminin kritik hata sonrasi crash dump uretttigini gosterir.",
                Evidence = bugChecks.Take(3).Select(ToEvidenceLine).ToList(),
                RecommendedActions = new()
                {
                    "C:\\Windows\\Minidump altindaki dump dosyalarini WinDbg/BlueScreenView ile analiz edin.",
                    "Ekran karti, chipset, NIC ve POS cevre birimi suruculerini guncelleyin.",
                    "RAM testi (mdsched) ve `sfc /scannow` + `DISM /Online /Cleanup-Image /RestoreHealth` calistirin."
                }
            });
        }

        if (whea.Count > 0)
        {
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = "Donanimsal / WHEA",
                Confidence = whea.Count >= 3 ? "Yuksek" : "Orta-Yuksek",
                Score = whea.Count >= 3 ? 92 : 80,
                Title = "Donanim seviyesinde hata (WHEA-Logger) tespit edildi",
                Why = "WHEA-Logger olaylari CPU, RAM veya PCI cihazlarinda dusuk seviye donanim hatasi bildirir. Genellikle BSOD veya reset oncesi gorulur.",
                Evidence = whea.Take(4).Select(ToEvidenceLine).ToList(),
                RecommendedActions = new()
                {
                    "RAM modullerini test edin (memtest86), gerekirse soketleri degistirin.",
                    "Anakart firmware/BIOS guncellemesini kontrol edin.",
                    "Bu cihaz icin donanim degisim planlamasi yapin — WHEA tekrarliyorsa donanim kotulesiyor."
                }
            });
        }

        if (diskErrors.Count > 0 || degradedDisks.Count > 0)
        {
            var strong = diskErrors.Count >= 2 || degradedDisks.Count > 0;
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = "Donanimsal / Disk",
                Confidence = strong ? "Yuksek" : "Orta",
                Score = strong ? 90 : 70,
                Title = "Disk veya dosya sistemi kaynakli kararsizlik",
                Why = "Disk I/O hatalari (event 11/51/153), NTFS bozulmasi (55/137) veya SMART kotulesmesi ani reset ve veri kaybi riski tasir.",
                Evidence = diskErrors.Take(5).Select(ToEvidenceLine)
                    .Concat(degradedDisks.Select(d => $"Disk saglik bulgusu: {d.DriveLetter} kullanim %{d.UsedPercent}, SMART={d.SmartStatus ?? "Yok"}"))
                    .Take(6).ToList(),
                RecommendedActions = new()
                {
                    "SMART degerlerini dogrulayin; PredFail/Degraded ise diski acilen degistirin.",
                    "Bakim penceresinde `chkdsk /f` (sistem disk icin restart sonrasi calisir) yapin.",
                    "Disk doluluk %95 uzerindeyse temp/log temizligi ve dosya tasima planlayin."
                }
            });
        }

        if (tdr.Count > 0)
        {
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = "Yazilimsal / GPU Surucusu (TDR)",
                Confidence = tdr.Count >= 3 ? "Yuksek" : "Orta",
                Score = tdr.Count >= 3 ? 78 : 60,
                Title = "GPU surucusu donup resetleniyor (TDR)",
                Why = "Display 4101 / nvlddmkm 13 olaylari GPU surucusunun cevap vermedigi ve Windows tarafindan resetlendigi anlamina gelir.",
                Evidence = tdr.Take(4).Select(ToEvidenceLine).ToList(),
                RecommendedActions = new()
                {
                    "GPU surucusunu temiz kurulum (DDU) ile guncel surume yukseltin.",
                    "Tekrarliyorsa donanim arizasi olabilir; baska GPU/onboard'a alip test edin.",
                    "Power plan ve PCIe Link State Power Management ayarlarini High Performance yapin."
                }
            });
        }

        if (unexpectedShutdowns.Count > 0)
        {
            var noOsCause = bugChecks.Count == 0 && diskErrors.Count == 0 && whea.Count == 0;
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = noOsCause ? "Guc / Donanim (Hard Reset)" : "Beklenmedik Kapanma",
                Confidence = noOsCause ? "Yuksek" : "Orta",
                Score = noOsCause ? 88 : 65,
                Title = noOsCause
                    ? $"Cihaz son {clampedHours}sa icinde {unexpectedShutdowns.Count} kez temiz kapanmadan resetlendi — yazilim kaniti yok"
                    : $"Beklenmedik kapanma {unexpectedShutdowns.Count} kez tekrarladi",
                Why = "Kernel-Power 41 ve EventLog 6008 olaylari isletim sisteminin normal shutdown akisi olmadan kapandigini gosterir. " +
                      (noOsCause ? "Loglarda BSOD/disk/WHEA kaniti yok — guc kaynagi, UPS veya watchdog reset suphesi yuksek." : ""),
                Evidence = unexpectedShutdowns.Take(5).Select(ToEvidenceLine).ToList(),
                RecommendedActions = new()
                {
                    "Magaza priz/UPS hattini kontrol edin; UPS battery test calistirin.",
                    "Ayni saatlerde tekrarliyorsa magaza elektrik dalgalanmasi/aydinlatma-kontaktor problemi olabilir.",
                    "Adaptor/PSU degistirin (POS PC'ler icin spare PSU stoklayin).",
                    "BIOS guc ayarlari ve watchdog/auto-restart-on-failure'i pasiflestirip izleyin."
                }
            });
        }

        if (serviceCrashes.Count > 0)
        {
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = "Yazilimsal / Servis",
                Confidence = serviceCrashes.Count >= 3 ? "Orta-Yuksek" : "Orta",
                Score = serviceCrashes.Count >= 3 ? 70 : 55,
                Title = "Windows veya uygulama servislerinde tekrarlayan cokme",
                Why = "Service Control Manager 7031/7034/7000 ailesi servis kararsizligi veya bagimlilik sorunlarini gosterir.",
                Evidence = serviceCrashes.Take(5).Select(ToEvidenceLine).ToList(),
                RecommendedActions = new()
                {
                    "Coken servisin adini olay detayindan cikarip bagimliliklarini kontrol edin.",
                    "Servis hesabi, startup timeout ve uygulama loglarini eslestirin.",
                    "Disk hung su belirtisi olabilir — disk performansini olcun."
                }
            });
        }

        if (appErrors.Count > 0)
        {
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = "Yazilimsal / Uygulama Cokmesi",
                Confidence = appErrors.Count >= 5 ? "Orta-Yuksek" : "Orta",
                Score = appErrors.Count >= 5 ? 65 : 50,
                Title = $"Uygulamalar son {clampedHours}sa icinde {appErrors.Count} kez coktu/donduu",
                Why = "Application Error 1000 / Hang 1002 / .NET 1026 olaylari kullanici uygulamalarinin abnormal sonlandigini gosterir.",
                Evidence = appErrors.Take(5).Select(ToEvidenceLine).ToList(),
                RecommendedActions = new()
                {
                    "Hangi uygulama oldugunu olay detayindan cikarin (Faulting application name).",
                    "Uygulama gunluklerine bakin, gerekirse uygulamayi guncelleyin/yeniden kurun.",
                    "Bagimli dll'lerin/runtime'in (VC++, .NET) eksiksiz oldugunu dogrulayin."
                }
            });
        }

        if (criticalTempSensors.Count > 0)
        {
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = "Donanimsal / Isi",
                Confidence = "Orta",
                Score = 60,
                Title = "Isinma kaynakli stabilite riski",
                Why = "Collector sicaklik sensorleri kritik seviyeye cikmis gorunuyor — termal throttling veya beklenmedik kapanmaya yol acabilir.",
                Evidence = criticalTempSensors.Select(t => $"{t.SensorName}: {t.TemperatureCelsius}°C, durum={t.Status ?? "Bilinmiyor"}").ToList(),
                RecommendedActions = new()
                {
                    "Fan, hava kanali ve toz temizligi yapin.",
                    "Kasa konumu ve havalandirma kosullarini kontrol edin.",
                    "Termal pad/macun yenileme planlayin (ozellikle eski POS PC'ler icin)."
                }
            });
        }

        if (networkErrors.Count >= 5)
        {
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = "Ag / Baglanti",
                Confidence = "Orta",
                Score = 45,
                Title = "Tekrarlayan ag baglanti hatalari",
                Why = "DNS/DHCP timeout, link drop veya IP conflict olaylari yogun. Bu genellikle kapanma sebebi degildir ama uygulamayi tetikleyebilir.",
                Evidence = networkErrors.Take(4).Select(ToEvidenceLine).ToList(),
                RecommendedActions = new()
                {
                    "Switch port, kablo ve NIC suruculerini kontrol edin.",
                    "DHCP scope dolu mu, DNS sunucu erisilebilir mi dogrulayin.",
                    "Birden cok cihaz etkileniyorsa magaza switch/router taraflari problemli olabilir."
                }
            });
        }

        if (updateFailures.Count > 0)
        {
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = "Yazilimsal / Guncelleme",
                Confidence = "Dusuk-Orta",
                Score = 35,
                Title = "Windows Update basarisizliklari",
                Why = "Update kurulumu/indirmesi sorunluysa sistem dosyalari etkilenebilir, sik restart denemesi olabilir.",
                Evidence = updateFailures.Take(3).Select(ToEvidenceLine).ToList(),
                RecommendedActions = new()
                {
                    "BITS ve Windows Update servislerini dogrulayin.",
                    "SoftwareDistribution klasoru temizligi ve Windows Update onarimi uygulayin."
                }
            });
        }

        if (userInitiatedShutdowns.Count > 0 && unexpectedShutdowns.Count == 0 && bugChecks.Count == 0)
        {
            hypotheses.Add(new EventLogHypothesisDto
            {
                Category = "Normal Restart",
                Confidence = "Yuksek",
                Score = 30,
                Title = "Kapanmalar planli/kullanici tarafindan tetiklenmis",
                Why = "USER32 1074 olaylari kim/hangi process tarafindan kapatildigini gosterir. Burada beklenmedik kapanma kaniti yok.",
                Evidence = userInitiatedShutdowns.Take(3).Select(ToEvidenceLine).ToList(),
                RecommendedActions = new()
                {
                    "1074 olayinin detayinda 'Reason' ve 'Process Name' alanlarini okuyun.",
                    "Update sonrasi otomatik restart, scheduled task veya kullanici tetigi olabilir."
                }
            });
        }

        hypotheses = hypotheses.OrderByDescending(h => h.Score).ToList();

        var top = hypotheses.FirstOrDefault();
        var classification = top?.Category ?? "Belirsiz";

        var overall = top != null
            ? top.Title
            : (normalizedEntries.Count == 0
                ? $"Son {clampedHours}sa icinde bu cihazdan kritik event log girisi yok."
                : "Loglarda net kok neden kaniti az. Daha uzun aralik veya dump/donanim verisi gerekebilir.");

        var dataQuality = new DataQualityDto
        {
            HasEventLogData = normalizedEntries.Count > 0,
            LatestEventLogReport = latestEventLogReport,
            HasUptimeData = latestUptime != null,
            HasDiskHealthData = latestDiskHealth != null && latestDiskHealth.Count > 0,
            HasTemperatureData = latestTemps != null && latestTemps.Count > 0
        };

        var timeline = normalizedEntries
            .Take(Math.Clamp(limit, 20, 1000))
            .Select(e => new EventLogTimelineItemDto
            {
                TimeGenerated = e.TimeGenerated,
                Source = e.Source,
                EventId = e.EventId,
                Level = e.Level,
                Summary = e.TranslatedMessage ?? TrimMessage(e.Message),
                RawMessage = TrimMessage(e.Message)
            })
            .ToList();

        return new EventLogAnalysisResultDto
        {
            DeviceId = deviceId,
            HoursAnalyzed = clampedHours,
            DataQuality = dataQuality,
            Summary = new EventLogAnalysisSummaryDto
            {
                OverallAssessment = overall,
                PrimaryCategory = classification,
                PrimaryConfidence = top?.Confidence ?? "Dusuk",
                HardwareLikely = hypotheses.Any(h => h.Category.StartsWith("Donanimsal", StringComparison.OrdinalIgnoreCase) && h.Score >= 70),
                SoftwareLikely = hypotheses.Any(h => h.Category.StartsWith("Yazilimsal", StringComparison.OrdinalIgnoreCase) && h.Score >= 70),
                UnexpectedShutdownCount = unexpectedShutdowns.Count,
                BlueScreenCount = bugChecks.Count,
                DiskIssueCount = diskErrors.Count,
                ServiceCrashCount = serviceCrashes.Count,
                NetworkIssueCount = networkErrors.Count,
                WheaCount = whea.Count,
                AppCrashCount = appErrors.Count,
                TdrCount = tdr.Count,
                LastBootTime = latestUptime?.BootTime,
                LastUnexpectedShutdownAt = unexpectedShutdowns.FirstOrDefault()?.TimeGenerated
            },
            Hypotheses = hypotheses,
            RecentTimeline = timeline,
            BootShutdownTimeline = bootShutdownTimeline,
            ShutdownChains = shutdownChains
        };
    }

    private static string ClassifyBootEvent(EventLogEntryDto e) => (e.Source, e.EventId) switch
    {
        ("EventLog", 6005) => "BootClean",
        ("EventLog", 6006) => "ShutdownClean",
        ("EventLog", 6008) => "ShutdownUnexpected",
        ("EventLog", 6013) => "UptimeReport",
        ("Microsoft-Windows-Kernel-Power", 41) => "ShutdownUnexpected",
        ("Microsoft-Windows-Kernel-General", 12) => "BootClean",
        ("Microsoft-Windows-Kernel-General", 13) => "ShutdownClean",
        ("USER32", 1074) => "UserShutdown",
        _ => "Other"
    };

    private async Task<T?> GetLatestJsonAsync<T>(string deviceId, string collectorName)
    {
        var json = await _db.CollectorReports
            .Where(r => r.DeviceId == deviceId && r.CollectorName == collectorName)
            .OrderByDescending(r => r.TimestampUtc)
            .Select(r => r.JsonData)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(json)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return default;
        }
    }

    private static bool IsUnexpectedShutdown(EventLogEntryDto e) =>
        (e.Source == "Microsoft-Windows-Kernel-Power" && e.EventId == 41) ||
        (e.Source == "EventLog" && e.EventId == 6008);

    private static bool IsDiskRelated(EventLogEntryDto e) =>
        (e.Source == "Disk" && (e.EventId == 7 || e.EventId == 11 || e.EventId == 15 || e.EventId == 51 || e.EventId == 153)) ||
        (e.Source == "Ntfs" && (e.EventId == 55 || e.EventId == 130 || e.EventId == 137)) ||
        (e.Source == "volmgr" && e.EventId == 161) ||
        (e.Source == "volsnap" && e.EventId == 25);

    private static bool IsServiceCrash(EventLogEntryDto e) =>
        e.Source == "Service Control Manager" &&
        (e.EventId is 7000 or 7001 or 7009 or 7011 or 7023 or 7024 or 7026 or 7031 or 7034 or 7038);

    private static string ToEvidenceLine(EventLogEntryDto e)
    {
        var summary = e.TranslatedMessage ?? TrimMessage(e.Message);
        return $"{e.TimeGenerated.ToLocalTime():dd.MM HH:mm} — {e.Source} #{e.EventId}: {summary}";
    }

    private static string TrimMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "-";
        var compact = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return compact.Length <= 200 ? compact : compact[..197] + "...";
    }
}

public sealed class EventLogAnalysisResultDto
{
    public string DeviceId { get; set; } = "";
    public int HoursAnalyzed { get; set; }
    public DataQualityDto DataQuality { get; set; } = new();
    public EventLogAnalysisSummaryDto Summary { get; set; } = new();
    public List<EventLogHypothesisDto> Hypotheses { get; set; } = new();
    public List<EventLogTimelineItemDto> RecentTimeline { get; set; } = new();
    public List<BootShutdownEventDto> BootShutdownTimeline { get; set; } = new();
    public List<ShutdownChainDto> ShutdownChains { get; set; } = new();
}

public sealed class DataQualityDto
{
    public bool HasEventLogData { get; set; }
    public DateTime? LatestEventLogReport { get; set; }
    public bool HasUptimeData { get; set; }
    public bool HasDiskHealthData { get; set; }
    public bool HasTemperatureData { get; set; }
}

public sealed class EventLogAnalysisSummaryDto
{
    public string OverallAssessment { get; set; } = "";
    public string PrimaryCategory { get; set; } = "";
    public string PrimaryConfidence { get; set; } = "";
    public bool HardwareLikely { get; set; }
    public bool SoftwareLikely { get; set; }
    public int UnexpectedShutdownCount { get; set; }
    public int BlueScreenCount { get; set; }
    public int DiskIssueCount { get; set; }
    public int ServiceCrashCount { get; set; }
    public int NetworkIssueCount { get; set; }
    public int WheaCount { get; set; }
    public int AppCrashCount { get; set; }
    public int TdrCount { get; set; }
    public DateTime? LastBootTime { get; set; }
    public DateTime? LastUnexpectedShutdownAt { get; set; }
}

public sealed class EventLogHypothesisDto
{
    public string Category { get; set; } = "";
    public string Confidence { get; set; } = "";
    public int Score { get; set; }
    public string Title { get; set; } = "";
    public string Why { get; set; } = "";
    public List<string> Evidence { get; set; } = new();
    public List<string> RecommendedActions { get; set; } = new();
}

public sealed class EventLogTimelineItemDto
{
    public DateTime TimeGenerated { get; set; }
    public string Source { get; set; } = "";
    public long EventId { get; set; }
    public string Level { get; set; } = "";
    public string Summary { get; set; } = "";
    public string RawMessage { get; set; } = "";
}

public sealed class BootShutdownEventDto
{
    public DateTime TimeGenerated { get; set; }
    public string Type { get; set; } = "";
    public string Source { get; set; } = "";
    public long EventId { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class ShutdownChainDto
{
    public DateTime ShutdownAt { get; set; }
    public string ShutdownSource { get; set; } = "";
    public long ShutdownEventId { get; set; }
    public List<EventLogTimelineItemDto> PrecedingEvents { get; set; } = new();
}
