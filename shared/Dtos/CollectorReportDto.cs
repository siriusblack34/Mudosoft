namespace Mudosoft.Shared.Dtos;

/// <summary>
/// Agent'tan backend'e gönderilen toplu collector raporu.
/// </summary>
public class CollectorReportDto
{
    public string DeviceId { get; set; } = "";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public List<CollectorResultDto> Results { get; set; } = new();
}

public class CollectorResultDto
{
    public string CollectorName { get; set; } = "";
    public DateTime TimestampUtc { get; set; }
    public string Severity { get; set; } = "Info";
    public string JsonData { get; set; } = "{}";
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

// ─── Port Monitor ───
public class PortCheckResultDto
{
    public int Port { get; set; }
    public string ServiceName { get; set; } = "";
    public bool IsOpen { get; set; }
    public int ResponseTimeMs { get; set; }
}

// ─── Process Usage ───
public class TopProcessDto
{
    public string Name { get; set; } = "";
    public int Pid { get; set; }
    public double CpuPercent { get; set; }
    public double RamMB { get; set; }
}

// ─── Service Monitor ───
public class ServiceStatusDto
{
    public string ServiceName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = ""; // Running, Stopped, etc.
    public string? ActionTaken { get; set; } // null, "Restarted", "RestartFailed"
    public string? ErrorMessage { get; set; }
}

// ─── Event Log ───
public class EventLogEntryDto
{
    public string LogName { get; set; } = "";       // System, Application
    public string Source { get; set; } = "";
    public long EventId { get; set; }
    public string Level { get; set; } = "";          // Error, Warning, Information
    public DateTime TimeGenerated { get; set; }
    public string Message { get; set; } = "";
    public string? TranslatedMessage { get; set; }   // Backend tarafında Türkçe açıklama
    public string? SuggestedAction { get; set; }      // Backend tarafında önerilen çözüm
}

// ─── Disk Health ───
public class DiskHealthDto
{
    public string DriveLetter { get; set; } = "";     // C:, D:
    public string Label { get; set; } = "";
    public string DriveType { get; set; } = "";       // Fixed, Removable, Network
    public string FileSystem { get; set; } = "";      // NTFS, FAT32
    public double TotalGB { get; set; }
    public double FreeGB { get; set; }
    public double UsedPercent { get; set; }
    public string? SmartStatus { get; set; }          // OK, Degraded, PredFail (WMI varsa)
}

// ─── Windows Update ───
public class WindowsUpdateDto
{
    public string Title { get; set; } = "";
    public string UpdateId { get; set; } = "";
    public DateTime? InstalledOn { get; set; }
    public bool IsInstalled { get; set; }
    public bool IsMandatory { get; set; }
    public string? Description { get; set; }
}

// ─── Temperature ───
public class TemperatureReadingDto
{
    public string SensorName { get; set; } = "";      // CPU, GPU, Disk, etc.
    public double TemperatureCelsius { get; set; }
    public string? Status { get; set; }               // Normal, Warning, Critical
}

// ─── UPS Status ───
public class UpsStatusDto
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";          // Online, OnBattery, LowBattery, Unknown
    public int? BatteryPercent { get; set; }
    public int? EstimatedRuntimeMinutes { get; set; }
    public string? Source { get; set; }               // WMI, APC_NMC, etc.
}

// ─── Network Speed ───
public class NetworkSpeedDto
{
    public double DownloadMbps { get; set; }
    public double UploadMbps { get; set; }
    public int LatencyMs { get; set; }
    public string TestServer { get; set; } = "";
    public DateTime TestedAt { get; set; }
}

// ─── Uptime Report ───
public class UptimeReportDto
{
    public DateTime BootTime { get; set; }
    public double UptimeHours { get; set; }
    public int UptimeDays { get; set; }
    public DateTime? LastShutdown { get; set; }
    public string ShutdownReason { get; set; } = "";   // Planned, Unexpected, Unknown
}

// ─── Scheduled Cleanup ───
public class CleanupResultDto
{
    public string TargetPath { get; set; } = "";
    public int FilesDeleted { get; set; }
    public double FreedMB { get; set; }
    public string? Error { get; set; }
}
