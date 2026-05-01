using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Shared.Dtos;
using System.Diagnostics;
using System.Management;
using System.Text.Json;

namespace Orchestra.Agent.Services.Collectors;

/// <summary>
/// Sistem uptime bilgisini toplar: boot zamanı, uptime süresi, son kapanma nedeni.
/// Environment.TickCount64 (Win7 uyumlu değil ama .NET 8 gerektirir) + WMI kullanır.
/// </summary>
public sealed class UptimeReportCollector : ICollector
{
    private readonly UptimeReportConfig _config;
    private readonly ILogger<UptimeReportCollector> _logger;

    public string Name => "UptimeReport";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public UptimeReportCollector(
        IOptions<CollectorsConfig> config,
        ILogger<UptimeReportCollector> logger)
    {
        _config = config.Value.UptimeReport;
        _logger = logger;
    }

    public Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var bootTime = GetBootTime();
        var uptime = DateTime.UtcNow - bootTime;
        var (lastShutdown, reason) = GetLastShutdownInfo();

        var report = new UptimeReportDto
        {
            BootTime = bootTime,
            UptimeHours = Math.Round(uptime.TotalHours, 1),
            UptimeDays = (int)uptime.TotalDays,
            LastShutdown = lastShutdown,
            ShutdownReason = reason
        };

        // 30 günden fazla uptime varsa uyarı (restart gerekebilir)
        var severity = "Info";
        if (uptime.TotalDays > 30)
        {
            severity = "Warning";
            _logger.LogWarning("System uptime is {Days} days - consider a restart", (int)uptime.TotalDays);
        }

        return Task.FromResult(new CollectorResult
        {
            CollectorName = Name,
            Success = true,
            Severity = severity,
            JsonData = JsonSerializer.Serialize(report)
        });
    }

    private DateTime GetBootTime()
    {
        try
        {
            // WMI ile kesin boot zamanı
            using var searcher = new ManagementObjectSearcher(
                "SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                var bootStr = obj["LastBootUpTime"]?.ToString();
                if (bootStr != null)
                {
                    var dt = ManagementDateTimeConverter.ToDateTime(bootStr);
                    return dt.ToUniversalTime();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WMI boot time query failed, using TickCount fallback");
        }

        // Fallback: Environment.TickCount64
        var tickMs = Environment.TickCount64;
        return DateTime.UtcNow.AddMilliseconds(-tickMs);
    }

    private (DateTime? LastShutdown, string Reason) GetLastShutdownInfo()
    {
        try
        {
            // Event Log'dan son kapanma olayını bul (Event ID 6006 = clean shutdown, 6008 = unexpected)
            using var eventLog = new EventLog("System");
            var entries = eventLog.Entries;

            for (int i = entries.Count - 1; i >= Math.Max(0, entries.Count - 500); i--)
            {
                try
                {
                    var entry = entries[i];
                    if (entry.Source != "EventLog") continue;

                    if (entry.InstanceId == 6006)
                    {
                        return (entry.TimeGenerated.ToUniversalTime(), "Planned");
                    }
                    if (entry.InstanceId == 6008)
                    {
                        return (entry.TimeGenerated.ToUniversalTime(), "Unexpected");
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read shutdown event");
        }

        return (null, "Unknown");
    }
}
