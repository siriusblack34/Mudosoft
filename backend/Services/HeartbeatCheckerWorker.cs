using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection; // 🔥 Factory için eklendi

namespace MudoSoft.Backend.Services
{
    public class HeartbeatCheckerWorker : BackgroundService
    {
        private readonly ILogger<HeartbeatCheckerWorker> _log;
        private readonly IServiceScopeFactory _scopeFactory; // 🔥 Factory eklendi.

        public HeartbeatCheckerWorker(IServiceScopeFactory scopeFactory, ILogger<HeartbeatCheckerWorker> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("HeartbeatCheckerWorker starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        // Repository'yi scoped olarak dinamik çek
                        var repo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>(); 
                        
                        // Cihaz durumlarını kontrol eden lojik burada olmalı
                        // await repo.CheckHeartbeatsAsync(); // Varsayımsal çağrı

                        _log.LogInformation("HeartbeatChecker cycle ran.");
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Heartbeat Checker cycle crashed");
                }

                // Her 1 dakika bekle (Örnek)
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _log.LogInformation("HeartbeatCheckerWorker stopped");
        }
    }
}