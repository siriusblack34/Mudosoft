using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services;

/// <summary>
/// Her sabah 08:00'de tüm kasa cihazlarında GeniusPOS'un çalışıp çalışmadığını kontrol eder.
/// Kontrol yöntemi: UNC yoluyla (\\IP\C$\GeniusPOS\) bugünün log dosyasının varlığını doğrular.
/// KasaLogController ile aynı erişim mekanizmasını kullanır.
/// </summary>
public class KasaMorningCheckWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<KasaMorningCheckWorker> _logger;

    private const int CheckHourLocal = 8; // 08:00 yerel saat

    public KasaMorningCheckWorker(IServiceProvider services, ILogger<KasaMorningCheckWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KasaMorningCheckWorker başladı. Her gün {Hour}:00'da kontrol yapacak.", CheckHourLocal);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now; // Yerel saat
            var nextRun = now.Date.AddHours(CheckHourLocal);

            // Eğer bugünün 08:00'i geçtiyse, yarınki 08:00'e bekle
            if (now >= nextRun)
                nextRun = nextRun.AddDays(1);

            var delay = nextRun - now;
            _logger.LogInformation("Sonraki kasa sabah kontrolü: {NextRun:dd.MM.yyyy HH:mm} ({Delay:hh\\:mm} sonra)", nextRun, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested) break;

            await RunCheckAsync(stoppingToken);
        }
    }

    public async Task RunCheckAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Kasa sabah kontrolü başlıyor...");

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

        // Sadece Kasa tipi, geçici kapalı olmayanlar
        var kasas = await db.StoreDevices
            .AsNoTracking()
            .Where(d => d.DeviceType.StartsWith("Kasa") && !d.IsTemporarilyClosed)
            .ToListAsync(ct);

        if (kasas.Count == 0)
        {
            _logger.LogWarning("Kontrol edilecek aktif kasa bulunamadı.");
            return;
        }

        _logger.LogInformation("{Count} kasa kontrol edilecek.", kasas.Count);

        var today = DateTime.Now;
        var dateDmy = today.ToString("ddMMyyyy"); // GeniusPOS log format: ddMMyyyy
        var dateYmd = today.ToString("yyyy_MM_dd"); // IMPDLL log format: yyyy_MM_dd
        var checkedAtUtc = DateTime.UtcNow;

        // Paralel kontrol — her kasa 6s timeout ile, 20 paralel
        var semaphore = new SemaphoreSlim(20, 20);
        var tasks = kasas.Select(kasa => Task.Run(async () =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await CheckKasaAsync(kasa, dateDmy, dateYmd, checkedAtUtc, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }, ct)).ToList();

        var results = await Task.WhenAll(tasks);

        // Toplu kaydet
        db.KasaMorningChecks.AddRange(results);
        await db.SaveChangesAsync(ct);

        var healthy = results.Count(r => r.IsHealthy);
        var unhealthy = results.Count(r => !r.IsHealthy);
        _logger.LogInformation("Kasa sabah kontrolü tamamlandı. Sağlıklı: {Healthy}, Sorunlu: {Unhealthy}", healthy, unhealthy);

        if (unhealthy > 0)
        {
            var problemList = results
                .Where(r => !r.IsHealthy)
                .Select(r => $"{r.StoreName} {r.DeviceType} ({r.IpAddress}): {r.ErrorMessage ?? "log bulunamadı"}")
                .ToList();
            _logger.LogWarning("Sorunlu kasalar:\n{Problems}", string.Join("\n", problemList));
        }
    }

    private async Task<KasaMorningCheck> CheckKasaAsync(
        StoreDevice kasa, string dateDmy, string dateYmd,
        DateTime checkedAtUtc, CancellationToken ct)
    {
        var check = new KasaMorningCheck
        {
            StoreDeviceId = kasa.DeviceId,
            StoreCode = kasa.StoreCode,
            StoreName = kasa.StoreName,
            DeviceType = kasa.DeviceType,
            IpAddress = kasa.CalculatedIpAddress ?? "",
            CheckedAt = checkedAtUtc,
        };

        if (string.IsNullOrWhiteSpace(kasa.CalculatedIpAddress))
        {
            check.ErrorMessage = "IP adresi tanımlı değil";
            return check;
        }

        // Her kasa için maksimum 6 saniye — UNC yanıt vermezse takılmasın
        using var kasaCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        kasaCts.CancelAfter(TimeSpan.FromSeconds(6));

        try
        {
            await Task.Run(() =>
            {
                var uncBase = $@"\\{kasa.CalculatedIpAddress}\C$\GeniusPOS";

                // 1. UNC paylaşımına erişilebilir mi?
                if (!Directory.Exists(uncBase))
                {
                    check.IsUncReachable = false;
                    check.ErrorMessage = "UNC paylaşımına erişilemiyor (ağ veya izin sorunu)";
                    return;
                }

                check.IsUncReachable = true;

                // 2. Bugünün GeniusPOS log dosyası var mı?
                var logFiles = Directory.GetFiles(uncBase, $"geniuspos_*_{dateDmy}.log");
                var impdllFiles = Directory.GetFiles(uncBase, $"IMPDLL_{dateYmd}*.*");

                if (logFiles.Length > 0 || impdllFiles.Length > 0)
                {
                    check.IsGeniusPosLogFound = true;
                    check.IsHealthy = true;
                }
                else
                {
                    check.IsGeniusPosLogFound = false;
                    check.IsHealthy = false;
                    check.ErrorMessage = $"Bugüne ait GeniusPOS log dosyası bulunamadı (kontrol: {DateTime.Now:HH:mm})";
                }
            }, kasaCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            check.IsUncReachable = false;
            check.ErrorMessage = "Zaman aşımı (6s) — kasa ağda yanıt vermiyor";
        }
        catch (UnauthorizedAccessException ex)
        {
            check.IsUncReachable = false;
            check.ErrorMessage = $"Erişim reddedildi: {ex.Message}";
            _logger.LogWarning("Kasa UNC erişim hatası: {DeviceId} - {Message}", kasa.DeviceId, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            check.ErrorMessage = ex.Message.Length > 490 ? ex.Message[..490] : ex.Message;
            _logger.LogError(ex, "Kasa kontrol hatası: {DeviceId}", kasa.DeviceId);
        }

        return check;
    }
}
