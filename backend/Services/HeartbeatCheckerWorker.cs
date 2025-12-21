using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MudoSoft.Backend.Data;

namespace MudoSoft.Backend.Services
{
    public class HeartbeatCheckerWorker : BackgroundService
    {
        private readonly ILogger<HeartbeatCheckerWorker> _log;
        private readonly IServiceScopeFactory _scopeFactory;
        
        // Heartbeat timeout süresi (2 dakika)
        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(2);
        // Kontrol aralığı (30 saniye)
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

        public HeartbeatCheckerWorker(IServiceScopeFactory scopeFactory, ILogger<HeartbeatCheckerWorker> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("HeartbeatCheckerWorker starting (timeout: {Timeout}, interval: {Interval})",
                HeartbeatTimeout, CheckInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndUpdateDeviceStatusAsync();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Heartbeat Checker cycle crashed");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }

            _log.LogInformation("HeartbeatCheckerWorker stopped");
        }

        private async Task CheckAndUpdateDeviceStatusAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MudoSoftDbContext>();

            var cutoffTime = DateTime.UtcNow - HeartbeatTimeout;

            // Online olan ama son heartbeat'i timeout süresinden eski olan cihazları bul
            var staleDevices = await dbContext.Devices
                .Where(d => d.Online && (d.LastSeen == null || d.LastSeen < cutoffTime))
                .ToListAsync();

            if (staleDevices.Count > 0)
            {
                foreach (var device in staleDevices)
                {
                    device.Online = false;
                    _log.LogInformation("Device {DeviceId} ({Hostname}) marked as OFFLINE (LastSeen: {LastSeen})",
                        device.Id, device.Hostname, device.LastSeen?.ToString("o") ?? "never");
                }

                await dbContext.SaveChangesAsync();
                _log.LogInformation("Marked {Count} device(s) as offline due to heartbeat timeout", staleDevices.Count);
            }
        }
    }
}