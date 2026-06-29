using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Services;
using System.Text.Json;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/service-monitor")]
public class ServiceMonitorController : ControllerBase
{
    private readonly OrchestraDbContext _db;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ServiceMonitorController> _log;

    private string ConfigFilePath => Path.Combine(_env.ContentRootPath, "service-monitor.json");

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public ServiceMonitorController(
        OrchestraDbContext db,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<ServiceMonitorController> logger)
    {
        _db = db;
        _config = config;
        _env = env;
        _log = logger;
    }

    // ─── CONFIG ─────────────────────────────────────────────────────────────

    public class ServiceDefinitionDto
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public ServiceDefinitionDto() { }
        public ServiceDefinitionDto(string name, string displayName) { Name = name; DisplayName = displayName; }
    }

    public class ServiceMonitorConfigDto
    {
        public bool Enabled { get; set; }
        public int IntervalSeconds { get; set; }
        public int ConfirmationThreshold { get; set; }
        public bool AutoStartStoppedServices { get; set; }
        public int MaxConcurrency { get; set; }
        public int WmiTimeoutSeconds { get; set; }
        public string[] DeviceTypes { get; set; } = Array.Empty<string>();
        public List<ServiceDefinitionDto> Services { get; set; } = new();
    }

    /// <summary>Mevcut servis monitör konfigürasyonunu döner.</summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var section = _config.GetSection("CriticalServiceMonitor");

        var services = section.GetSection("Services").Get<List<ServiceDefinitionDto>>();
        if (services == null || services.Count == 0)
        {
            services = new List<ServiceDefinitionDto>
            {
                new("MSSQL$SQLEXPRESS",              "SQL Server (SQLEXPRESS)"),
                new("GeniusFileTransferServiceInbound",  "GeniusFileTransferServiceInbound"),
                new("GeniusFileTransferServiceOutbound", "GeniusFileTransferServiceOutbound"),
                new("GeniusFullImporter",            "GeniusFullImporter"),
                new("GeniusXMLExporter",             "GeniusXMLExporter"),
                new("GeniusXMLImporter",             "GeniusXMLImporter"),
                new("GeniusXMLReceiver",             "GeniusXMLReceiver"),
                new("GeniusXMLSender",               "GeniusXMLSender"),
            };
        }

        var deviceTypes = section.GetSection("DeviceTypes").Get<string[]>();
        if (deviceTypes == null || deviceTypes.Length == 0)
            deviceTypes = new[] { "PC" };

        var dto = new ServiceMonitorConfigDto
        {
            Enabled                 = section.GetValue("Enabled", true),
            IntervalSeconds         = section.GetValue("IntervalSeconds", 300),
            ConfirmationThreshold   = section.GetValue("ConfirmationThreshold", 2),
            AutoStartStoppedServices = section.GetValue("AutoStartStoppedServices", true),
            MaxConcurrency          = section.GetValue("MaxConcurrency", 4),
            WmiTimeoutSeconds       = section.GetValue("WmiTimeoutSeconds", 8),
            DeviceTypes             = deviceTypes,
            Services                = services,
        };

        return Ok(new
        {
            config = dto,
            lastScanAt = CriticalServiceMonitorWorker.LastScanAt,
            configFileExists = System.IO.File.Exists(ConfigFilePath)
        });
    }

    /// <summary>Konfigürasyonu güncelle (service-monitor.json'a yazar, değişiklikler anında devreye girer).</summary>
    [HttpPut("config")]
    public async Task<IActionResult> UpdateConfig([FromBody] ServiceMonitorConfigDto dto)
    {
        var wrapper = new { CriticalServiceMonitor = dto };
        var json = JsonSerializer.Serialize(wrapper, _jsonOpts);
        await System.IO.File.WriteAllTextAsync(ConfigFilePath, json);

        _log.LogInformation("ServiceMonitor config updated via dashboard by {User}", User.Identity?.Name);
        return Ok(new { message = "Ayarlar kaydedildi", config = dto });
    }

    /// <summary>Varsayılan ayarlara sıfırla (service-monitor.json'u siler).</summary>
    [HttpDelete("config")]
    public IActionResult ResetConfig()
    {
        if (System.IO.File.Exists(ConfigFilePath))
            System.IO.File.Delete(ConfigFilePath);
        return Ok(new { message = "Varsayılan ayarlara sıfırlandı" });
    }

    // ─── TRIGGER ────────────────────────────────────────────────────────────

    /// <summary>Taramayı hemen tetikle (aralığı beklemeden).</summary>
    [HttpPost("trigger")]
    public IActionResult TriggerScan()
    {
        CriticalServiceMonitorWorker.TriggerNow();
        _log.LogInformation("Service monitor scan triggered manually by {User}", User.Identity?.Name);
        return Accepted(new { message = "Tarama tetiklendi" });
    }

    // ─── REMOTE SERVICE CONTROL ─────────────────────────────────────────────

    public class ServiceControlRequest
    {
        public string IpAddress { get; set; } = "";
        public string ServiceName { get; set; } = "";
        /// <summary>start veya stop</summary>
        public string Action { get; set; } = "start";
    }

    /// <summary>
    /// Uzak makinede bir servisi başlat veya durdur (sc.exe üzerinden).
    /// POST: /api/service-monitor/control
    /// </summary>
    [HttpPost("control")]
    public async Task<IActionResult> ControlService([FromBody] ServiceControlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IpAddress) || string.IsNullOrWhiteSpace(request.ServiceName))
            return BadRequest(new { error = "IpAddress ve ServiceName gereklidir" });

        if (request.Action != "start" && request.Action != "stop")
            return BadRequest(new { error = "Action 'start' veya 'stop' olmalıdır" });

        try
        {
            var action = request.Action == "start" ? "start" : "stop";
            var (exitCode, output) = await RunScCommand(request.IpAddress, action, request.ServiceName);

            var success = exitCode == 0;
            _log.LogInformation("ServiceControl {Action} {Service}@{Ip}: exit={Exit}",
                action, request.ServiceName, request.IpAddress, exitCode);

            return Ok(new
            {
                success,
                action,
                service = request.ServiceName,
                ip = request.IpAddress,
                output = output.Trim(),
                exitCode
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static async Task<(int exitCode, string output)> RunScCommand(string ip, string action, string service)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $@"\\{ip} {action} {service}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return (proc.ExitCode, stdout + stderr);
    }

    // ─── INCIDENTS ──────────────────────────────────────────────────────────

    [HttpGet("incidents/active")]
    public async Task<IActionResult> GetActiveIncidents(CancellationToken ct)
    {
        var incidents = await _db.StoreServiceIncidents
            .AsNoTracking()
            .Where(i => i.ResolvedAt == null)
            .OrderBy(i => i.Severity == "Warning")
            .ThenBy(i => i.StoreCode)
            .ThenBy(i => i.DeviceName)
            .ThenBy(i => i.DisplayName)
            .Select(i => new
            {
                i.Id, i.DeviceId, i.StoreCode, i.StoreName, i.DeviceName, i.IpAddress,
                i.ServiceName, i.DisplayName, i.Status, i.Severity, i.Message,
                i.LastStartMode, i.LastError, i.ConsecutiveFailures,
                i.FirstDetectedAt, i.LastDetectedAt, i.ResolvedAt
            })
            .ToListAsync(ct);

        return Ok(incidents);
    }

    [HttpGet("incidents/recent")]
    public async Task<IActionResult> GetRecentIncidents([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        hours = Math.Clamp(hours, 1, 168);
        var since = DateTime.UtcNow.AddHours(-hours);

        var incidents = await _db.StoreServiceIncidents
            .AsNoTracking()
            .Where(i => i.FirstDetectedAt >= since || i.LastDetectedAt >= since || i.ResolvedAt >= since)
            .OrderByDescending(i => i.ResolvedAt == null)
            .ThenByDescending(i => i.LastDetectedAt)
            .Take(200)
            .Select(i => new
            {
                i.Id, i.DeviceId, i.StoreCode, i.StoreName, i.DeviceName, i.IpAddress,
                i.ServiceName, i.DisplayName, i.Status, i.Severity, i.Message,
                i.LastStartMode, i.LastError, i.ConsecutiveFailures,
                i.FirstDetectedAt, i.LastDetectedAt, i.ResolvedAt
            })
            .ToListAsync(ct);

        return Ok(incidents);
    }
}
