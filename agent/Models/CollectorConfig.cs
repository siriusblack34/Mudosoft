namespace Orchestra.Agent.Models;

public sealed class CollectorsConfig
{
    public PortMonitorConfig PortMonitor { get; set; } = new();
    public ProcessUsageConfig ProcessUsage { get; set; } = new();
    public ServiceMonitorConfig ServiceMonitor { get; set; } = new();
    public EventLogConfig EventLog { get; set; } = new();
    public DiskHealthConfig DiskHealth { get; set; } = new();
    public WindowsUpdateConfig WindowsUpdate { get; set; } = new();
    public TemperatureConfig Temperature { get; set; } = new();
    public UpsStatusConfig UpsStatus { get; set; } = new();
    public NetworkSpeedConfig NetworkSpeed { get; set; } = new();
    public UptimeReportConfig UptimeReport { get; set; } = new();
    public ScheduledCleanupConfig ScheduledCleanup { get; set; } = new();
}

public class CollectorBaseConfig
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
}

public sealed class PortMonitorConfig : CollectorBaseConfig
{
    public List<PortEntry> Ports { get; set; } = new();
    public int TimeoutMs { get; set; } = 3000;
}

public sealed class PortEntry
{
    public int Port { get; set; }
    public string ServiceName { get; set; } = "";
}

public sealed class ProcessUsageConfig : CollectorBaseConfig
{
    public int TopCount { get; set; } = 10;
}

public sealed class ServiceMonitorConfig : CollectorBaseConfig
{
    public List<string> MonitoredServices { get; set; } = new();
    public bool AutoRestart { get; set; } = true;
    public int MaxRestartsPerHour { get; set; } = 3;
}

public sealed class EventLogConfig : CollectorBaseConfig
{
    public new int IntervalSeconds { get; set; } = 300; // 5 dakika
    public List<string> LogNames { get; set; } = new();
    public int MaxEventsPerCycle { get; set; } = 50;
}

public sealed class DiskHealthConfig : CollectorBaseConfig
{
    public new int IntervalSeconds { get; set; } = 3600; // 1 saat
}

public sealed class WindowsUpdateConfig : CollectorBaseConfig
{
    public new int IntervalSeconds { get; set; } = 3600; // 1 saat
}

public sealed class TemperatureConfig : CollectorBaseConfig
{
    public new int IntervalSeconds { get; set; } = 60;
}

public sealed class UpsStatusConfig : CollectorBaseConfig
{
    public new int IntervalSeconds { get; set; } = 30;
}

public sealed class NetworkSpeedConfig : CollectorBaseConfig
{
    public new int IntervalSeconds { get; set; } = 3600; // 1 saat
    public string TestUrl { get; set; } = "http://speedtest.tele2.net/10MB.zip"; // Ücretsiz test dosyası
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class UptimeReportConfig : CollectorBaseConfig
{
    public new int IntervalSeconds { get; set; } = 600; // 10 dakika
}

public sealed class ScheduledCleanupConfig : CollectorBaseConfig
{
    public new int IntervalSeconds { get; set; } = 86400; // 24 saat
    public List<CleanupTarget> Targets { get; set; } = new();
}

public sealed class CleanupTarget
{
    public string Path { get; set; } = "";
    public int MaxAgeDays { get; set; } = 7;
}
