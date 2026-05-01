namespace Orchestra.Backend.Services
{
    /// <summary>
    /// RouterLatencySamples tablosunu 7 gunden eski kayitlardan temizleyen periyodik worker.
    /// Gunde 1 kez calisir.
    /// </summary>
    public class RouterLatencyPurgeWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RouterLatencyPurgeWorker> _log;
        private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
        private const int RetainDays = 7;

        public RouterLatencyPurgeWorker(IServiceScopeFactory scopeFactory, ILogger<RouterLatencyPurgeWorker> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Ilk calistirma: baslangictan 10 dk sonra (DB hazir olsun)
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var detection = scope.ServiceProvider.GetRequiredService<MobileLineDetectionService>();
                    var deleted = await detection.PurgeOldSamplesAsync(RetainDays, stoppingToken);
                    if (deleted > 0)
                        _log.LogInformation("RouterLatencyPurgeWorker deleted {Count} old samples (>{Days} days)", deleted, RetainDays);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "RouterLatencyPurgeWorker purge failed");
                }

                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
