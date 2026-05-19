using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using System.Security;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Shared.Dtos;

namespace Orchestra.Backend.Services;

/// <summary>
/// Hedef cihazin Windows Event Log'unu backend'den dogrudan RPC uzerinden ceker.
/// Domain credentials (MudoSoft:Wmi) ile EventLogSession acar — agent gerekmez.
/// Sonucu CollectorReports tablosuna "EventLog" adi altinda kaydeder ki mevcut analyzer kullanabilsin.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RemoteEventLogPullService
{
    private readonly OrchestraDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<RemoteEventLogPullService> _logger;

    public RemoteEventLogPullService(
        OrchestraDbContext db,
        IConfiguration config,
        ILogger<RemoteEventLogPullService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<RemotePullResult> PullAsync(string deviceId, int hours, CancellationToken ct = default)
    {
        var host = await ResolveHostAsync(deviceId);
        if (host == null)
            return RemotePullResult.Fail("Cihaz icin IP/hostname bulunamadi.");

        var creds = LoadCredentials();
        if (creds == null)
            return RemotePullResult.Fail("Domain credentials eksik (MudoSoft:Wmi yapilandirmasini kontrol edin).");

        var clamped = Math.Clamp(hours, 1, 24 * 30);
        var since = DateTime.UtcNow.AddHours(-clamped).ToString("yyyy-MM-ddTHH:mm:ss.000Z");

        // Level 1=Critical, 2=Error, 3=Warning
        var xpath = $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime>='{since}']]]";

        var entries = new List<EventLogEntryDto>();
        var errors = new List<string>();

        foreach (var logName in new[] { "System", "Application" })
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var batch = await Task.Run(() => ReadEventLog(host, logName, xpath, creds), ct);
                entries.AddRange(batch);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Remote event log read failed: host={Host} log={Log}", host, logName);
                errors.Add($"{logName}: {ex.Message}");
            }
        }

        if (entries.Count == 0 && errors.Count > 0)
            return RemotePullResult.Fail(string.Join(" | ", errors));

        var ordered = entries
            .OrderByDescending(e => e.TimeGenerated)
            .Take(1000)
            .ToList();

        var resolvedDeviceId = await ResolveStoredDeviceIdAsync(deviceId);
        var json = JsonSerializer.Serialize(ordered);

        _db.CollectorReports.Add(new CollectorReport
        {
            DeviceId = resolvedDeviceId,
            CollectorName = "EventLog",
            TimestampUtc = DateTime.UtcNow,
            Severity = ordered.Any(e => e.Level == "Error" || e.Level == "Critical") ? "Warning" : "Info",
            JsonData = json,
            Success = true,
            ErrorMessage = errors.Count > 0 ? string.Join(" | ", errors) : null
        });
        await _db.SaveChangesAsync(ct);

        return RemotePullResult.Ok(host, ordered.Count, resolvedDeviceId, errors);
    }

    private static List<EventLogEntryDto> ReadEventLog(
        string host,
        string logName,
        string xpath,
        DomainCredentials creds)
    {
        using var session = new EventLogSession(
            host,
            creds.Domain,
            creds.Username,
            creds.SecurePassword,
            SessionAuthentication.Negotiate);

        var query = new EventLogQuery(logName, PathType.LogName, xpath)
        {
            Session = session,
            ReverseDirection = true
        };

        using var reader = new EventLogReader(query);
        var entries = new List<EventLogEntryDto>(256);

        EventRecord? record;
        while ((record = reader.ReadEvent()) != null)
        {
            using (record)
            {
                string? message = null;
                try { message = record.FormatDescription(); } catch { }

                var level = record.Level switch
                {
                    1 => "Critical",
                    2 => "Error",
                    3 => "Warning",
                    _ => record.LevelDisplayName ?? "Info"
                };

                entries.Add(new EventLogEntryDto
                {
                    LogName = logName,
                    Source = record.ProviderName ?? "",
                    EventId = record.Id,
                    Level = level,
                    TimeGenerated = (record.TimeCreated ?? DateTime.UtcNow).ToUniversalTime(),
                    Message = Truncate(message, 500)
                });

                if (entries.Count >= 500) break;
            }
        }

        return entries;
    }

    private async Task<string?> ResolveHostAsync(string deviceId)
    {
        // 1) Agent kayitli mi? IP'sini al.
        var agentIp = await _db.Devices.AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => d.IpAddress)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(agentIp))
            return agentIp;

        // 2) StoreDevices'da CalculatedIpAddress var mi?
        var storeIp = await _db.StoreDevices.AsNoTracking()
            .Where(s => s.DeviceId == deviceId)
            .Select(s => s.CalculatedIpAddress)
            .FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(storeIp) ? null : storeIp;
    }

    private async Task<string> ResolveStoredDeviceIdAsync(string requestedDeviceId)
    {
        // Eger StoreDevices ID'siyse ve ayni IP'ye sahip agentli cihaz varsa, agent ID altinda kaydet.
        var storeDevice = await _db.StoreDevices.AsNoTracking()
            .Where(s => s.DeviceId == requestedDeviceId)
            .Select(s => new { s.CalculatedIpAddress })
            .FirstOrDefaultAsync();

        if (storeDevice != null && !string.IsNullOrWhiteSpace(storeDevice.CalculatedIpAddress))
        {
            var matchedAgent = await _db.Devices.AsNoTracking()
                .Where(d => d.IpAddress == storeDevice.CalculatedIpAddress)
                .OrderByDescending(d => d.LastSeen)
                .Select(d => d.Id)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(matchedAgent))
                return matchedAgent;
        }

        return requestedDeviceId;
    }

    private DomainCredentials? LoadCredentials()
    {
        var section = _config.GetSection("MudoSoft:Wmi");
        var domain = section["Domain"];
        var username = section["Username"];
        var passwordKey = section["PasswordSecretKey"];

        if (string.IsNullOrWhiteSpace(username))
            return null;

        var password = !string.IsNullOrWhiteSpace(passwordKey)
            ? Environment.GetEnvironmentVariable(passwordKey)
            : null;
        password ??= Environment.GetEnvironmentVariable("MUDOSOFT_WMI_PASSWORD")
                  ?? Environment.GetEnvironmentVariable("WMI_PASSWORD")
                  ?? section["Password"];

        if (string.IsNullOrWhiteSpace(password))
            return null;

        var secure = new SecureString();
        foreach (var c in password) secure.AppendChar(c);
        secure.MakeReadOnly();

        return new DomainCredentials(domain ?? "", username, secure);
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= max ? value : value[..max] + "...";
    }

    private sealed record DomainCredentials(string Domain, string Username, SecureString SecurePassword);
}

public sealed class RemotePullResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Host { get; init; }
    public int EventCount { get; init; }
    public string? StoredAsDeviceId { get; init; }
    public List<string> PartialErrors { get; init; } = new();

    public static RemotePullResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };

    public static RemotePullResult Ok(string host, int count, string storedAs, List<string> partial) => new()
    {
        Success = true,
        Host = host,
        EventCount = count,
        StoredAsDeviceId = storedAs,
        PartialErrors = partial
    };
}
