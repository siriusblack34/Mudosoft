using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Orchestra.Backend.Services;

public class DeviceHealthBreakdown
{
    public string DeviceId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public int StoreCode { get; set; }
    public string? StoreName { get; set; }
    public int Score { get; set; }
    public string Status { get; set; } = "Unknown";
    public bool Online { get; set; }
    public DateTime? LastSeen { get; set; }
    public float CpuPercent { get; set; }
    public float RamPercent { get; set; }
    public float DiskPercent { get; set; }
    public List<HealthDeduction> Deductions { get; set; } = new();
    public int PreviousScore { get; set; }
    public string TrendDirection { get; set; } = "Stable"; // Up, Down, Stable
}

public class HealthDeduction
{
    public string Category { get; set; } = "";
    public string Reason { get; set; } = "";
    public int Points { get; set; }
}

public class HealthScoreSummary
{
    public int TotalDevices { get; set; }
    public int Healthy { get; set; }
    public int Warning { get; set; }
    public int Risky { get; set; }
    public int Critical { get; set; }
    public int Offline { get; set; }
    public double AverageScore { get; set; }
    public List<DeviceHealthBreakdown> Bottom10 { get; set; } = new();
    public List<StoreHealthAverage> StoreAverages { get; set; } = new();
}

public class StoreHealthAverage
{
    public int StoreCode { get; set; }
    public string? StoreName { get; set; }
    public double AverageScore { get; set; }
    public int DeviceCount { get; set; }
    public int CriticalCount { get; set; }
    public string Status { get; set; } = "Healthy";
}

public class HealthScoreService
{
    private readonly OrchestraDbContext _db;
    private readonly ILogger<HealthScoreService> _logger;

    private const string LATEST_AGENT_VERSION = "1.0.0.74";

    public HealthScoreService(OrchestraDbContext db, ILogger<HealthScoreService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HealthScoreSummary> GetSummaryAsync()
    {
        var devices = await _db.Devices
            .AsNoTracking()
            .Where(d => !d.HiddenForNonAdmins || true)
            .ToListAsync();

        var allBreakdowns = new List<DeviceHealthBreakdown>();

        // Fetch latest collector data in bulk for efficiency
        var allDeviceIds = devices.Select(d => d.Id).ToList();
        var latestReports = await GetLatestCollectorReportsAsync(allDeviceIds);
        var previousScores = await GetPreviousScoresAsync(allDeviceIds);

        foreach (var device in devices)
        {
            latestReports.TryGetValue(device.Id, out var reports);
            previousScores.TryGetValue(device.Id, out var prevScore);
            var breakdown = CalculateScore(device, reports, prevScore);
            allBreakdowns.Add(breakdown);

            // Persist updated score to device
            var dbDevice = await _db.Devices.FindAsync(device.Id);
            if (dbDevice != null)
            {
                dbDevice.HealthScore = breakdown.Score;
                dbDevice.HealthStatus = breakdown.Status;
            }
        }

        await _db.SaveChangesAsync();

        var summary = new HealthScoreSummary
        {
            TotalDevices = allBreakdowns.Count,
            Healthy = allBreakdowns.Count(d => d.Status == "Healthy"),
            Warning = allBreakdowns.Count(d => d.Status == "Warning"),
            Risky = allBreakdowns.Count(d => d.Status == "Risky"),
            Critical = allBreakdowns.Count(d => d.Status == "Critical"),
            Offline = allBreakdowns.Count(d => !d.Online),
            AverageScore = allBreakdowns.Count > 0 ? Math.Round(allBreakdowns.Average(d => d.Score), 1) : 0,
            Bottom10 = allBreakdowns
                .Where(d => d.Online || d.LastSeen.HasValue)
                .OrderBy(d => d.Score)
                .Take(10)
                .ToList(),
            StoreAverages = allBreakdowns
                .Where(d => d.StoreCode > 0)
                .GroupBy(d => d.StoreCode)
                .Select(g => new StoreHealthAverage
                {
                    StoreCode = g.Key,
                    StoreName = g.First().StoreName,
                    AverageScore = Math.Round(g.Average(d => d.Score), 1),
                    DeviceCount = g.Count(),
                    CriticalCount = g.Count(d => d.Status == "Critical"),
                    Status = g.Average(d => d.Score) >= 80 ? "Healthy"
                           : g.Average(d => d.Score) >= 60 ? "Warning"
                           : g.Average(d => d.Score) >= 40 ? "Risky" : "Critical"
                })
                .OrderBy(s => s.AverageScore)
                .ToList()
        };

        return summary;
    }

    public async Task<List<DeviceHealthBreakdown>> GetCriticalDevicesAsync()
    {
        var devices = await _db.Devices.AsNoTracking().ToListAsync();
        var allDeviceIds = devices.Select(d => d.Id).ToList();
        var latestReports = await GetLatestCollectorReportsAsync(allDeviceIds);

        return devices
            .Select(d =>
            {
                latestReports.TryGetValue(d.Id, out var reports);
                return CalculateScore(d, reports, null);
            })
            .Where(d => d.Status == "Critical")
            .OrderBy(d => d.Score)
            .ToList();
    }

    public async Task<List<DeviceHealthBreakdown>> GetRiskyDevicesAsync()
    {
        var devices = await _db.Devices.AsNoTracking().ToListAsync();
        var allDeviceIds = devices.Select(d => d.Id).ToList();
        var latestReports = await GetLatestCollectorReportsAsync(allDeviceIds);

        return devices
            .Select(d =>
            {
                latestReports.TryGetValue(d.Id, out var reports);
                return CalculateScore(d, reports, null);
            })
            .Where(d => d.Status == "Risky")
            .OrderBy(d => d.Score)
            .ToList();
    }

    public async Task<DeviceHealthBreakdown> GetDeviceScoreAsync(string deviceId)
    {
        var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deviceId)
            ?? throw new KeyNotFoundException($"Device {deviceId} not found");

        var reports = await GetLatestCollectorReportsForDeviceAsync(deviceId);
        return CalculateScore(device, reports, null);
    }

    // ── Core Score Calculation ──────────────────────────────────────────────

    private DeviceHealthBreakdown CalculateScore(
        Device device,
        Dictionary<string, CollectorReport>? reports,
        int? previousScore)
    {
        var deductions = new List<HealthDeduction>();
        var score = 100;

        // 1. Last Seen / Online status
        var minutesSinceLastSeen = device.LastSeen.HasValue
            ? (DateTime.UtcNow - device.LastSeen.Value).TotalMinutes
            : double.MaxValue;

        if (!device.Online || minutesSinceLastSeen > 60)
        {
            Deduct(deductions, "Bağlantı", "Cihaz çevrimdışı (>1 saat)", 25);
        }
        else if (minutesSinceLastSeen > 10)
        {
            Deduct(deductions, "Bağlantı", "Heartbeat gecikmeli (>10 dk)", 10);
        }

        // 2. CPU Usage
        if (device.CurrentCpuUsagePercent > 95)
            Deduct(deductions, "CPU", $"CPU kritik: %{device.CurrentCpuUsagePercent:0}", 20);
        else if (device.CurrentCpuUsagePercent > 85)
            Deduct(deductions, "CPU", $"CPU yüksek: %{device.CurrentCpuUsagePercent:0}", 12);
        else if (device.CurrentCpuUsagePercent > 70)
            Deduct(deductions, "CPU", $"CPU artışta: %{device.CurrentCpuUsagePercent:0}", 5);

        // 3. RAM Usage
        if (device.CurrentRamUsagePercent > 95)
            Deduct(deductions, "RAM", $"RAM kritik: %{device.CurrentRamUsagePercent:0}", 18);
        else if (device.CurrentRamUsagePercent > 85)
            Deduct(deductions, "RAM", $"RAM yüksek: %{device.CurrentRamUsagePercent:0}", 10);
        else if (device.CurrentRamUsagePercent > 75)
            Deduct(deductions, "RAM", $"RAM artışta: %{device.CurrentRamUsagePercent:0}", 4);

        // 4. Disk C: Usage
        if (device.CurrentDiskUsagePercent > 98)
            Deduct(deductions, "Disk C:", $"Disk C: kritik dolu: %{device.CurrentDiskUsagePercent:0}", 20);
        else if (device.CurrentDiskUsagePercent > 90)
            Deduct(deductions, "Disk C:", $"Disk C: dolu: %{device.CurrentDiskUsagePercent:0}", 12);
        else if (device.CurrentDiskUsagePercent > 80)
            Deduct(deductions, "Disk C:", $"Disk C: dolmak üzere: %{device.CurrentDiskUsagePercent:0}", 5);

        // 5. Disk D: Usage
        if (device.CurrentDiskDUsagePercent.HasValue)
        {
            var dUsage = device.CurrentDiskDUsagePercent.Value;
            if (dUsage > 95)
                Deduct(deductions, "Disk D:", $"Disk D: kritik dolu: %{dUsage:0}", 15);
            else if (dUsage > 85)
                Deduct(deductions, "Disk D:", $"Disk D: dolu: %{dUsage:0}", 8);
        }

        // 6. Service Monitor (from CollectorReport)
        if (reports != null && reports.TryGetValue("ServiceMonitor", out var svcReport))
        {
            try
            {
                var services = JsonSerializer.Deserialize<List<ServiceStatusJson>>(svcReport.JsonData,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (services != null)
                {
                    var stoppedCount = services.Count(s => s.Status == "Stopped");
                    var geniusStopped = services.Any(s =>
                        (s.ServiceName.Contains("Genius", StringComparison.OrdinalIgnoreCase) ||
                         s.ServiceName.Contains("POS", StringComparison.OrdinalIgnoreCase)) &&
                        s.Status == "Stopped");

                    if (geniusStopped)
                        Deduct(deductions, "Servisler", "Genius/POS servisi durdurulmuş", 20);
                    else if (stoppedCount >= 3)
                        Deduct(deductions, "Servisler", $"{stoppedCount} kritik servis durdurulmuş", 15);
                    else if (stoppedCount > 0)
                        Deduct(deductions, "Servisler", $"{stoppedCount} servis durdurulmuş", 8);
                }
            }
            catch { /* malformed JSON — skip */ }
        }

        // 7. Event Log (recent critical errors)
        if (reports != null && reports.TryGetValue("EventLog", out var evtReport))
        {
            if (evtReport.Severity == "Critical")
                Deduct(deductions, "Event Log", "Kritik Windows hataları tespit edildi", 15);
            else if (evtReport.Severity == "Warning")
                Deduct(deductions, "Event Log", "Uyarı düzeyi Windows olayları", 5);
        }

        // 8. Network latency
        if (reports != null && reports.TryGetValue("NetworkSpeed", out var netReport))
        {
            try
            {
                using var doc = JsonDocument.Parse(netReport.JsonData);
                if (doc.RootElement.TryGetProperty("LatencyMs", out var latProp) ||
                    doc.RootElement.TryGetProperty("latencyMs", out latProp))
                {
                    var latency = latProp.GetInt32();
                    if (latency > 500)
                        Deduct(deductions, "Ağ", $"Yüksek gecikme: {latency}ms", 10);
                    else if (latency > 200)
                        Deduct(deductions, "Ağ", $"Gecikme artışı: {latency}ms", 4);
                }
            }
            catch { /* skip */ }
        }

        // 9. Agent version
        if (!string.IsNullOrEmpty(device.AgentVersion) && device.AgentVersion != LATEST_AGENT_VERSION)
        {
            Deduct(deductions, "Agent", $"Agent versiyonu eski: {device.AgentVersion}", 5);
        }

        // 10. Remote Desktop Helper
        if (!device.VncInstalled)
            Deduct(deductions, "Remote Desktop", "VNC kurulu değil", 5);

        // Calculate final score
        score = Math.Max(0, score - deductions.Sum(d => d.Points));

        var status = score >= 80 ? "Healthy"
                   : score >= 60 ? "Warning"
                   : score >= 40 ? "Risky"
                   : "Critical";

        var trend = previousScore.HasValue
            ? (score > previousScore.Value + 5 ? "Up"
               : score < previousScore.Value - 5 ? "Down" : "Stable")
            : "Stable";

        return new DeviceHealthBreakdown
        {
            DeviceId = device.Id,
            Hostname = device.Hostname,
            IpAddress = device.IpAddress,
            StoreCode = device.StoreCode,
            StoreName = device.StoreName,
            Score = score,
            Status = status,
            Online = device.Online,
            LastSeen = device.LastSeen,
            CpuPercent = device.CurrentCpuUsagePercent,
            RamPercent = device.CurrentRamUsagePercent,
            DiskPercent = device.CurrentDiskUsagePercent,
            Deductions = deductions,
            PreviousScore = previousScore ?? score,
            TrendDirection = trend
        };
    }

    private static void Deduct(List<HealthDeduction> list, string category, string reason, int points)
    {
        list.Add(new HealthDeduction { Category = category, Reason = reason, Points = points });
    }

    // ── Data Access Helpers ────────────────────────────────────────────────

    private async Task<Dictionary<string, Dictionary<string, CollectorReport>>> GetLatestCollectorReportsAsync(
        List<string> deviceIds)
    {
        var cutoff = DateTime.UtcNow.AddHours(-4);
        var reports = await _db.CollectorReports
            .AsNoTracking()
            .Where(r => deviceIds.Contains(r.DeviceId) && r.TimestampUtc >= cutoff)
            .OrderByDescending(r => r.TimestampUtc)
            .ToListAsync();

        // Group by device -> collector, take latest per collector
        return reports
            .GroupBy(r => r.DeviceId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.CollectorName)
                      .ToDictionary(cg => cg.Key, cg => cg.First())
            );
    }

    private async Task<Dictionary<string, int>> GetPreviousScoresAsync(List<string> deviceIds)
    {
        return await _db.Devices
            .AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.HealthScore);
    }

    private async Task<Dictionary<string, CollectorReport>?> GetLatestCollectorReportsForDeviceAsync(string deviceId)
    {
        var cutoff = DateTime.UtcNow.AddHours(-4);
        var reports = await _db.CollectorReports
            .AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.TimestampUtc >= cutoff)
            .OrderByDescending(r => r.TimestampUtc)
            .ToListAsync();

        return reports
            .GroupBy(r => r.CollectorName)
            .ToDictionary(g => g.Key, g => g.First());
    }

    // ── Private DTOs ──────────────────────────────────────────────────────

    private class ServiceStatusJson
    {
        public string ServiceName { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
