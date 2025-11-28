using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace MudoSoft.Backend.Services
{
    // Bu servis, arka planda periyodik olarak cihaz durumlarını kontrol eder
    public class HeartbeatCheckerWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<HeartbeatCheckerWorker> _logger;
        
        // Her 15 saniyede bir kontrol et
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(15); 
        
        // 30 saniyeden fazla Heartbeat gelmezse OFFLINE say
        private readonly TimeSpan _offlineTolerance = TimeSpan.FromSeconds(30); 

        public HeartbeatCheckerWorker(IServiceProvider serviceProvider, ILogger<HeartbeatCheckerWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Heartbeat Checker Worker starting.");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                // Kontrol aralığı kadar bekle
                await Task.Delay(_checkInterval, stoppingToken);

                try
                {
                    await CheckDeviceStatusesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while checking device statuses.");
                }
            }
            _logger.LogInformation("Heartbeat Checker Worker stopping.");
        }

        private async Task CheckDeviceStatusesAsync(CancellationToken stoppingToken)
        {
            // Worker içinde DbContext'i kullanmak için scope oluşturulur
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<MudoSoftDbContext>();
                
                var offlineThreshold = DateTime.UtcNow.Subtract(_offlineTolerance);

                // LastSeen zamanı eşiği aşan ve hala Online olan cihazları bul
                var devicesToUpdate = await dbContext.Devices
                    // Online olan cihazları filtrele (LastSeen'in null olmasını engeller)
                    .Where(d => d.Online && d.LastSeen != null && d.LastSeen < offlineThreshold) 
                    .ToListAsync(stoppingToken);

                if (devicesToUpdate.Count > 0)
                {
                    foreach (var device in devicesToUpdate)
                    {
                        device.Online = false;
                        _logger.LogWarning("Device {DeviceId} ({Hostname}) timed out and set to OFFLINE.", device.Id, device.Hostname);
                    }

                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
        }
    }
}