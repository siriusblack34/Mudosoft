using System;
using System.IO;
using System.Diagnostics; // PerformanceCounter için eklendi
using Microsoft.Extensions.Logging; // Hata yönetimi için eklendi
using Mudosoft.Agent.Interfaces;

namespace Mudosoft.Agent.Services
{
    // Cihazın gerçek sistem bilgilerini toplayan ve PerformanceCounter kaynaklarını yöneten servis.
    public class SystemInfoService : ISystemInfoService, IDisposable
    {
        private readonly ILogger<SystemInfoService> _logger;
        private readonly PerformanceCounter? _cpuCounter;
        private readonly PerformanceCounter? _ramCounter;
        private readonly Random _rnd = new(); // Hata durumları için rastgele veri (fallback)

        public SystemInfoService(ILogger<SystemInfoService> logger)
        {
            _logger = logger;

            // PerformanceCounter'lar Windows'a özeldir ve ilk okuma için hazırlanmalıdır.
            try
            {
                // % CPU Kullanımı: Bu sayaç, son NextValue() çağrısından bu yana geçen süredeki ortalamayı döndürür.
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // İlk okuma için çağrılır (genellikle 0 döner)
                
                // RAM Kullanımı: % Committed Bytes In Use, toplam sanal bellek taahhüdünün yüzdesini verir,
                // Fiziksel RAM kullanımına en yakın pratik değerlerden biridir.
                _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                _ramCounter.NextValue(); // İlk okuma için çağrılır
            }
            catch (Exception ex)
            {
                // Sayaçlar başlatılamazsa (Örn: Windows olmayan bir işletim sistemi) rastgele veriye düşülür.
                _logger.LogError(ex, "PerformanceCounter başlatılamadı. Uygulama simülasyon verileriyle devam edecek.");
                _cpuCounter = null;
                _ramCounter = null;
            }
        }

        public double GetCpuUsage()
        {
            if (_cpuCounter != null)
            {
                try
                {
                    // Gerçek CPU kullanımını döndür.
                    return Math.Round(_cpuCounter.NextValue(), 1);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CPU verisi alınamadı. Rastgele veri fallback.");
                }
            }
            return _rnd.Next(1, 40);
        }

        public double GetRamUsage()
        {
            if (_ramCounter != null)
            {
                try
                {
                    // Gerçek RAM kullanım yüzdesini döndür.
                    return Math.Round(_ramCounter.NextValue(), 1); 
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RAM verisi alınamadı. Rastgele veri fallback.");
                }
            }
            return _rnd.Next(20, 80);
        }

        public double GetDiskUsage()
        {
            try
            {
                // Agent'ın yüklü olduğu ana diski (genellikle C:\) bulalım.
                var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:";
                var drive = new DriveInfo(systemDrive);
                
                if (drive.IsReady)
                {
                    // Toplam kullanılan alanın yüzde değerini hesapla
                    double used = drive.TotalSize - drive.AvailableFreeSpace;
                    double percent = (used / (double)drive.TotalSize) * 100.0;
                    return Math.Round(percent, 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Disk verisi alınamadı. Rastgele veri fallback.");
            }
            // Fallback:
            return _rnd.Next(10, 70);
        }

        public void Dispose()
        {
            // PerformanceCounter kaynaklarını serbest bırak.
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}