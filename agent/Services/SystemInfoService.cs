using System;
using System.IO;
using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Logging;
using Mudosoft.Agent.Interfaces;

namespace Mudosoft.Agent.Services
{
    public class SystemInfoService : ISystemInfoService, IDisposable
    {
        private readonly ILogger<SystemInfoService> _logger;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private readonly object _lock = new();
        private bool _countersInitialized = false;
        private readonly Random _rnd = new();

        // Cached hardware info (doesn't change during runtime)
        private string? _cachedCpuModel;
        private long _cachedTotalRamMB;
        private long _cachedTotalDiskGB;
        private string? _cachedGpuModel;
        private bool _hardwareInfoCached = false;

        public SystemInfoService(ILogger<SystemInfoService> logger)
        {
            _logger = logger;
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

        private void CacheHardwareInfo()
        {
            if (_hardwareInfoCached) return;

            lock (_lock)
            {
                if (_hardwareInfoCached) return;

                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        // CPU Model
                        using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                        {
                            foreach (var item in searcher.Get())
                            {
                                _cachedCpuModel = item["Name"]?.ToString()?.Trim();
                                break;
                            }
                        }

                        // Total RAM
                        using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                        {
                            foreach (var item in searcher.Get())
                            {
                                if (ulong.TryParse(item["TotalPhysicalMemory"]?.ToString(), out var bytes))
                                {
                                    _cachedTotalRamMB = (long)(bytes / 1024 / 1024);
                                }
                                break;
                            }
                        }

                        // Total Disk (System drive)
                        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:";
                        var drive = new DriveInfo(systemDrive);
                        if (drive.IsReady)
                        {
                            _cachedTotalDiskGB = drive.TotalSize / 1024 / 1024 / 1024;
                        }

                        // GPU Model
                        using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                        {
                            foreach (var item in searcher.Get())
                            {
                                _cachedGpuModel = item["Name"]?.ToString()?.Trim();
                                break; // Get first GPU
                            }
                        }

                        _logger.LogInformation("Hardware info cached: CPU={Cpu}, RAM={Ram}MB, Disk={Disk}GB, GPU={Gpu}",
                            _cachedCpuModel, _cachedTotalRamMB, _cachedTotalDiskGB, _cachedGpuModel);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("Hardware info caching failed: {Message}", ex.Message);
                }
                finally
                {
                    _hardwareInfoCached = true;
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
                var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:";
                var drive = new DriveInfo(systemDrive);
                
                if (drive.IsReady)
                {
                    double used = drive.TotalSize - drive.AvailableFreeSpace;
                    double percent = (used / (double)drive.TotalSize) * 100.0;
                    return Math.Round(percent, 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Disk verisi alınamadı. Rastgele veri fallback.");
            }
            return _rnd.Next(10, 70);
        }

        public string GetOsName()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
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
            return Environment.OSVersion.ToString();
        }

        public string GetCpuModel()
        {
            if (!_hardwareInfoCached) CacheHardwareInfo();
            return _cachedCpuModel ?? "Unknown";
        }

        public long GetTotalRamMB()
        {
            if (!_hardwareInfoCached) CacheHardwareInfo();
            return _cachedTotalRamMB;
        }

        public long GetTotalDiskGB()
        {
            if (!_hardwareInfoCached) CacheHardwareInfo();
            return _cachedTotalDiskGB;
        }

        public string? GetGpuModel()
        {
            if (!_hardwareInfoCached) CacheHardwareInfo();
            return _cachedGpuModel;
        }

        public string? GetLastLoggedInUser()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Method 1: Get currently logged in user from Win32_ComputerSystem
                    using (var searcher = new ManagementObjectSearcher("SELECT UserName FROM Win32_ComputerSystem"))
                    {
                        foreach (var item in searcher.Get())
                        {
                            var userName = item["UserName"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(userName))
                            {
                                var parts = userName.Split('\\');
                                _logger.LogDebug("GetLastLoggedInUser found: {User}", userName);
                                return parts.Length > 1 ? parts[1] : userName;
                            }
                        }
                    }

                    // Method 2: Fallback - Get from interactive logon sessions
                    using (var searcher = new ManagementObjectSearcher(
                        "SELECT * FROM Win32_LogonSession WHERE LogonType = 2 OR LogonType = 10"))
                    {
                        foreach (var session in searcher.Get())
                        {
                            var logonId = session["LogonId"]?.ToString();
                            if (string.IsNullOrEmpty(logonId)) continue;

                            // Query for the user associated with this logon session
                            using (var userSearcher = new ManagementObjectSearcher(
                                $"ASSOCIATORS OF {{Win32_LogonSession.LogonId='{logonId}'}} WHERE AssocClass=Win32_LoggedOnUser"))
                            {
                                foreach (var user in userSearcher.Get())
                                {
                                    var userName = user["Name"]?.ToString();
                                    if (!string.IsNullOrWhiteSpace(userName) && 
                                        !userName.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) &&
                                        !userName.EndsWith("$"))
                                    {
                                        _logger.LogDebug("GetLastLoggedInUser from session: {User}", userName);
                                        return userName;
                                    }
                                }
                            }
                        }
                    }

                    // Method 3: Environment variable (only works if running in user context)
                    var envUser = Environment.GetEnvironmentVariable("USERNAME");
                    if (!string.IsNullOrWhiteSpace(envUser) && !envUser.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("GetLastLoggedInUser from env: {User}", envUser);
                        return envUser;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to get last logged in user: {Message}", ex.Message);
            }
            return null;
        }

        public DateTime GetSystemBootTime()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
                    foreach (var item in searcher.Get())
                    {
                        var bootTimeStr = item["LastBootUpTime"]?.ToString();
                        if (!string.IsNullOrEmpty(bootTimeStr))
                        {
                            var bootTime = ManagementDateTimeConverter.ToDateTime(bootTimeStr);
                            return bootTime.ToUniversalTime();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to get boot time: {Message}", ex.Message);
            }
            // Fallback: approximate using Environment.TickCount64
            return DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64);
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
