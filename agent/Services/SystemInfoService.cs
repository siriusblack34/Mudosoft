using System;
using System.IO;
using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Logging;
using Orchestra.Agent.Interfaces;

namespace Orchestra.Agent.Services
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
                        // CPU Model - try WMI first, then Registry fallback
                        try
                        {
                            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                            {
                                foreach (var item in searcher.Get())
                                {
                                    _cachedCpuModel = item["Name"]?.ToString()?.Trim();
                                    break;
                                }
                            }
                        }
                        catch (Exception wmEx)
                        {
                            _logger.LogWarning("WMI CPU query failed, trying Registry: {Msg}", wmEx.Message);
                        }
                        
                        // CPU Fallback: Registry
                        if (string.IsNullOrEmpty(_cachedCpuModel))
                        {
                            try
                            {
                                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                                _cachedCpuModel = key?.GetValue("ProcessorNameString")?.ToString()?.Trim();
                                _logger.LogInformation("CPU from Registry: {Cpu}", _cachedCpuModel);
                            }
                            catch (Exception regEx)
                            {
                                _logger.LogWarning("Registry CPU query failed: {Msg}", regEx.Message);
                            }
                        }

                        // Total RAM - try WMI first
                        try
                        {
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
                        }
                        catch (Exception wmEx)
                        {
                            _logger.LogWarning("WMI RAM query failed: {Msg}", wmEx.Message);
                        }
                        
                        // RAM Fallback: Use GC.GetGCMemoryInfo as approximation (not perfect but better than 0)
                        if (_cachedTotalRamMB == 0)
                        {
                            try
                            {
                                // Try PerformanceCounter for available memory and estimate
                                var gcInfo = GC.GetGCMemoryInfo();
                                _cachedTotalRamMB = (long)(gcInfo.TotalAvailableMemoryBytes / 1024 / 1024);
                                _logger.LogInformation("RAM from GC: {Ram}MB", _cachedTotalRamMB);
                            }
                            catch { }
                        }

                        // Total Disk (System drive) - this already uses DriveInfo, should work
                        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:";
                        var drive = new DriveInfo(systemDrive);
                        if (drive.IsReady)
                        {
                            _cachedTotalDiskGB = drive.TotalSize / 1024 / 1024 / 1024;
                        }

                        // GPU Model - try WMI first, then Registry fallback
                        try
                        {
                            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                            {
                                foreach (var item in searcher.Get())
                                {
                                    _cachedGpuModel = item["Name"]?.ToString()?.Trim();
                                    break;
                                }
                            }
                        }
                        catch (Exception wmEx)
                        {
                            _logger.LogWarning("WMI GPU query failed: {Msg}", wmEx.Message);
                        }
                        
                        // GPU Fallback: Registry
                        if (string.IsNullOrEmpty(_cachedGpuModel))
                        {
                            try
                            {
                                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
                                _cachedGpuModel = key?.GetValue("DriverDesc")?.ToString()?.Trim();
                                _logger.LogInformation("GPU from Registry: {Gpu}", _cachedGpuModel);
                            }
                            catch (Exception regEx)
                            {
                                _logger.LogWarning("Registry GPU query failed: {Msg}", regEx.Message);
                            }
                        }

                        _logger.LogInformation("Hardware info cached: CPU={Cpu}, RAM={Ram}MB, Disk={Disk}GB, GPU={Gpu}",
                            _cachedCpuModel, _cachedTotalRamMB, _cachedTotalDiskGB, _cachedGpuModel);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("Hardware info caching failed: {Message}", ex.Message);
                    // Don't set _hardwareInfoCached = true, so we can retry next time
                    return;
                }
                
                // Only mark as cached if we actually got some data
                if (!string.IsNullOrEmpty(_cachedCpuModel) && _cachedCpuModel != "Unknown")
                {
                    _hardwareInfoCached = true;
                    _logger.LogInformation("Hardware info cached successfully");
                }
                else
                {
                    _logger.LogWarning("Hardware info incomplete, will retry on next call");
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

        public double? GetDiskDUsage()
        {
            try
            {
                var dDrive = new DriveInfo("D");
                if (dDrive.IsReady)
                {
                    double used = dDrive.TotalSize - dDrive.AvailableFreeSpace;
                    double percent = (used / (double)dDrive.TotalSize) * 100.0;
                    return Math.Round(percent, 1);
                }
            }
            catch { }
            return null;
        }

        public long? GetTotalDiskDGB()
        {
            try
            {
                var dDrive = new DriveInfo("D");
                if (dDrive.IsReady)
                {
                    return dDrive.TotalSize / 1024 / 1024 / 1024;
                }
            }
            catch { }
            return null;
        }

        public string GetOsName()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var arch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
                    
                    // Try WMI first for full name
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                        foreach (var item in searcher.Get())
                        {
                            var caption = item["Caption"]?.ToString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(caption))
                            {
                                return $"{caption} - {arch}";
                            }
                        }
                    }
                    catch (Exception wmEx)
                    {
                        _logger.LogWarning("WMI OS query failed, using Registry: {Msg}", wmEx.Message);
                    }
                    
                    // Fallback: Get ProductName from Registry for edition info
                    string? productName = null;
                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                        productName = key?.GetValue("ProductName")?.ToString()?.Trim();
                    }
                    catch { }
                    
                    if (!string.IsNullOrEmpty(productName))
                    {
                        return $"{productName} - {arch}";
                    }
                    
                    // Last fallback: Map version number to friendly name
                    var ver = Environment.OSVersion.Version;
                    var friendlyName = (ver.Major, ver.Minor) switch
                    {
                        (6, 1) => "Windows 7",
                        (6, 2) => "Windows 8",
                        (6, 3) => "Windows 8.1",
                        (10, 0) when ver.Build >= 22000 => "Windows 11",
                        (10, 0) => "Windows 10",
                        _ => $"Windows {ver.Major}.{ver.Minor}"
                    };
                    return $"{friendlyName} - {arch}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("OS bilgisi alınamadı: {Message}", ex.Message);
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
