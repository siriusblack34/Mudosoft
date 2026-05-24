using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;

namespace Orchestra.Backend.Services;

/// <summary>
/// Telemetri tablolarinda 45 gunden eski satirlari sessizce siler.
/// Hedef tablolar:
///   - DeviceMetrics (heartbeat metrikleri, dakikada bir × 250+ cihaz)
///   - CollectorReports (kolektor cikti satirlari)
///   - RouterLatencySamples (router ping sample'lari)
/// Olcek: ham toplam 15GB DB icinde bu 3 tablo ~%97. Retention olmadan
/// bir kac ay icinde disk dolar — bkz. db_size 2026-05-20.
/// Gunde 1 calisir, herhangi bir tablo > 200k satir silmesi gerekiyorsa
/// 100k'lik batch'ler ile siler (LOCK contention dusurmek icin).
/// </summary>
public class TelemetryRetentionWorker : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    private const int RetentionDays = 45;
    private const int BatchSize = 100_000;
    private const int MaxBatchesPerRun = 200; // 20M satir/run ust siniri

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TelemetryRetentionWorker> _logger;

    public TelemetryRetentionWorker(IServiceProvider serviceProvider, ILogger<TelemetryRetentionWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TelemetryRetentionWorker hata");
            }

            try { await Task.Delay(RunInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        _logger.LogInformation("TelemetryRetention basliyor: cutoff={Cutoff:o}", cutoff);

        var totalMetrics = await DeleteBatchedAsync(
            "\"DeviceMetrics\"", "\"TimestampUtc\"", cutoff, ct);
        var totalReports = await DeleteBatchedAsync(
            "\"CollectorReports\"", "\"TimestampUtc\"", cutoff, ct);
        var totalRouter = await DeleteBatchedAsync(
            "\"RouterLatencySamples\"", "\"SampledAt\"", cutoff, ct);

        _logger.LogInformation("TelemetryRetention bitti: DeviceMetrics={M} CollectorReports={C} RouterLatencySamples={R}",
            totalMetrics, totalReports, totalRouter);
    }

    private async Task<long> DeleteBatchedAsync(string table, string tsColumn, DateTime cutoff, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

        long total = 0;
        for (int i = 0; i < MaxBatchesPerRun; i++)
        {
            if (ct.IsCancellationRequested) break;

            // CTID ile batch'li silme — LIMIT direkt DELETE'de yok, alt sorgu ile yapilir.
            var sql = $"DELETE FROM {table} WHERE ctid IN (SELECT ctid FROM {table} WHERE {tsColumn} < {{0}} LIMIT {BatchSize})";
            var affected = await db.Database.ExecuteSqlRawAsync(sql, new object[] { cutoff }, ct);
            total += affected;
            if (affected < BatchSize) break; // bitti
            await Task.Delay(500, ct); // diger query'lere nefes
        }
        return total;
    }
}
