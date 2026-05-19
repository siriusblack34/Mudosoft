using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Orchestra.Backend.Services
{
    public class SchedulerBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SchedulerBackgroundService> _logger;
        private readonly PeriodicTimer _timer;

        public SchedulerBackgroundService(IServiceProvider serviceProvider, ILogger<SchedulerBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _timer = new PeriodicTimer(TimeSpan.FromMinutes(1)); // Her dakika kontrol et
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("⏳ Scheduler Service başladý.");

            while (await _timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckAndExecuteTasksAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Scheduler hatasý!");
                }
            }
        }

        private async Task CheckAndExecuteTasksAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var cleanupService = scope.ServiceProvider.GetRequiredService<IInboxCleanupService>();
            var stockCleanupService = scope.ServiceProvider.GetRequiredService<IStockCleanupService>();

            var now = DateTime.UtcNow;

            // Zamaný gelmiþ ve aktif görevleri bul
            var tasksToRun = await db.ScheduledTasks
                .Where(t => t.IsActive && t.NextRunTime <= now)
                .ToListAsync(stoppingToken);

            if (!tasksToRun.Any()) return;

            _logger.LogInformation("🔔 {Count} adet zamanlanmýþ görev tetikleniyor...", tasksToRun.Count);

            foreach (var task in tasksToRun)
            {
                _logger.LogInformation("🚀 Görev Baþlatýldý: {Id} ({Type})", task.Id, task.TaskType);

                try
                {
                    if (task.TaskType == "InboxCleanup")
                    {
                        var result = await cleanupService.CleanAllAsync();
                        task.LastResult = $"Success ({result.successCount}/{result.totalCount} deleted)";
                        _logger.LogInformation("✅ Görev Tamamlandý: {Result}", task.LastResult);
                    }
                    else if (task.TaskType == "StockCleanup")
                    {
                        var r = await stockCleanupService.CleanStoresWithErrorsAsync(stoppingToken);
                        task.LastResult = $"Success (cleaned={r.CleanedCount}, skipped={r.SkippedCleanCount}, offline={r.OfflineCount}, error={r.ErrorCount}, total={r.TotalChecked})";
                        _logger.LogInformation("✅ StockCleanup Tamamlandı: {Result}", task.LastResult);
                    }
                    else
                    {
                        task.LastResult = "Unknown Task Type";
                        _logger.LogWarning("⚠️ Bilinmeyen Görev Tipi: {Type}", task.TaskType);
                    }
                }
                catch (Exception ex)
                {
                    task.LastResult = $"Error: {ex.Message}";
                    _logger.LogError(ex, "❌ Görev Hatasý: {Id}", task.Id);
                }

                task.LastRunTime = DateTime.UtcNow;

                // NextRunTime güncelleme
                if (task.Frequency == "OneTime")
                {
                    task.IsActive = false; // Tek seferlikse pasife çek
                }
                else if (task.Frequency == "Daily")
                {
                    // Bir sonraki gün aynı saat (Yerel saatten hesaplayıp UTC'ye çevir)
                    var targetTime = task.TargetTime ?? DateTime.Now.TimeOfDay;
                    
                    // Yarın aynı saat (Local)
                    var nextRunLocal = DateTime.Now.Date.AddDays(1).Add(targetTime);
                    
                    // UTC olarak kaydet
                    task.NextRunTime = nextRunLocal.ToUniversalTime();

                     _logger.LogInformation("📅 Bir sonraki çalışma (UTC): {NextRun}", task.NextRunTime);
                }

                await db.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
