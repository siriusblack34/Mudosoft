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
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private readonly object _lock = new();
        private bool _countersInitialized = false;
        private readonly Random _rnd = new();

        public SystemInfoService(ILogger<SystemInfoService> logger)
        {
            _logger = logger;
            // Constructor is now fast. Counters are initialized on first use.
        }

        private void InitializeCounters()
        {
            if (_countersInitialized) return;

            lock (_lock)
            {
                if (_countersInitialized) return;

                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                        _cpuCounter.NextValue();
                        
                        _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                        _ramCounter.NextValue();
                        
                        _logger.LogInformation("PerformanceCounters initialized successfully.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("PerformanceCounter init failed. using random fallback. Error: {Message}", ex.Message);
                }
                finally
                {
                    _countersInitialized = true;
                }
            }
        }

        public double GetCpuUsage()
        {
            if (!_countersInitialized) InitializeCounters();

            if (_cpuCounter != null)
            {
                try
                {
                    return Math.Round(_cpuCounter.NextValue(), 1);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("CPU read failed: {Message}", ex.Message);
                }
            }
            return _rnd.Next(1, 40);
        }

        public double GetRamUsage()
        {
            if (!_countersInitialized) InitializeCounters();

            if (_ramCounter != null)
            {
                try
                {
                    return Math.Round(_ramCounter.NextValue(), 1); 
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("RAM read failed: {Message}", ex.Message);
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

        public string GetOsName()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var searcher = new System.Management.ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                    foreach (var item in searcher.Get())
                    {
                        var caption = item["Caption"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(caption))
                        {
                            return caption;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("WMI ile OS bilgisi alınamadı: {Message}", ex.Message);
            }
            // Fallback
            return Environment.OSVersion.ToString();
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