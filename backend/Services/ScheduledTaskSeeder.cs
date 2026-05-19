using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Orchestra.Backend.Services
{
    /// <summary>
    /// Sabit zamanlanmış görevleri (örn. StockCleanup 07:00 daily) DB'de yoksa oluşturur.
    /// Aktif kullanıcı silerse tekrar oluşturmaz — sadece "hiç görev yok" durumunda seed eder.
    /// </summary>
    public class ScheduledTaskSeeder : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ScheduledTaskSeeder> _log;

        public ScheduledTaskSeeder(IServiceScopeFactory scopeFactory, ILogger<ScheduledTaskSeeder> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

                var exists = await db.ScheduledTasks
                    .AnyAsync(t => t.TaskType == "StockCleanup", cancellationToken);

                if (exists)
                {
                    _log.LogInformation("ScheduledTaskSeeder: StockCleanup görevi zaten mevcut, seed atlandı.");
                    return;
                }

                var targetTime = new TimeSpan(7, 0, 0); // 07:00 (Local)
                var todayTarget = DateTime.Today.Add(targetTime);
                var nextRunLocal = todayTarget <= DateTime.Now
                    ? todayTarget.AddDays(1)
                    : todayTarget;

                var task = new ScheduledTask
                {
                    TaskType = "StockCleanup",
                    Frequency = "Daily",
                    TargetTime = targetTime,
                    NextRunTime = nextRunLocal.ToUniversalTime(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                db.ScheduledTasks.Add(task);
                await db.SaveChangesAsync(cancellationToken);

                _log.LogInformation("ScheduledTaskSeeder: StockCleanup günlük 07:00 görevi seed edildi (NextRun UTC: {NextRun})", task.NextRunTime);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "ScheduledTaskSeeder: StockCleanup seed başarısız");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
