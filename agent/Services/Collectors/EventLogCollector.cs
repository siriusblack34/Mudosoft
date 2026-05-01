using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Shared.Dtos;
using System.Diagnostics;
using System.Text.Json;

namespace Orchestra.Agent.Services.Collectors;

/// <summary>
/// Windows Event Log'larından Error ve Warning seviyesindeki olayları toplar.
/// Her çalışmada sadece son döngüden bu yana oluşan yeni olayları gönderir.
/// Win7 ve Win11 uyumlu - System.Diagnostics.EventLog kullanır.
/// </summary>
public sealed class EventLogCollector : ICollector
{
    private readonly EventLogConfig _config;
    private readonly ILogger<EventLogCollector> _logger;

    // Her log için son okunan zaman damgası
    private readonly Dictionary<string, DateTime> _lastReadTime = new();

    public string Name => "EventLog";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public EventLogCollector(
        IOptions<CollectorsConfig> config,
        ILogger<EventLogCollector> logger)
    {
        _config = config.Value.EventLog;
        _logger = logger;
    }

    public Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var allEntries = new List<EventLogEntryDto>();

        foreach (var logName in _config.LogNames)
        {
            try
            {
                var entries = ReadLogEntries(logName);
                allEntries.AddRange(entries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading EventLog '{LogName}'", logName);
            }
        }

        // En yeni olayları üstte göster, limit uygula
        var limited = allEntries
            .OrderByDescending(e => e.TimeGenerated)
            .Take(_config.MaxEventsPerCycle)
            .ToList();

        var hasCritical = limited.Any(e => e.Level == "Error");

        var result = new CollectorResult
        {
            CollectorName = Name,
            Success = true,
            Severity = hasCritical ? "Warning" : "Info",
            JsonData = JsonSerializer.Serialize(limited)
        };

        return Task.FromResult(result);
    }

    private List<EventLogEntryDto> ReadLogEntries(string logName)
    {
        var entries = new List<EventLogEntryDto>();

        // Son okuma zamanını al (ilk seferde son 5 dakikayı oku)
        if (!_lastReadTime.TryGetValue(logName, out var since))
        {
            since = DateTime.UtcNow.AddMinutes(-5);
        }

        using var eventLog = new EventLog(logName);
        var allEntries = eventLog.Entries;

        // Sondan başa doğru oku (en yeni girişler sonda)
        for (int i = allEntries.Count - 1; i >= 0; i--)
        {
            try
            {
                var entry = allEntries[i];
                var entryTimeUtc = entry.TimeGenerated.ToUniversalTime();

                // Zaten okunanları atla
                if (entryTimeUtc <= since)
                    break;

                // Sadece Error ve Warning seviyesini topla
                if (entry.EntryType != EventLogEntryType.Error &&
                    entry.EntryType != EventLogEntryType.Warning)
                    continue;

                entries.Add(new EventLogEntryDto
                {
                    LogName = logName,
                    Source = entry.Source,
                    EventId = entry.InstanceId,
                    Level = entry.EntryType.ToString(),
                    TimeGenerated = entryTimeUtc,
                    Message = TruncateMessage(entry.Message, 500)
                });

                // Çok fazla okumasın
                if (entries.Count >= _config.MaxEventsPerCycle)
                    break;
            }
            catch
            {
                // Bazı entry'ler erişilemez olabilir, atla
            }
        }

        // Son okuma zamanını güncelle
        _lastReadTime[logName] = DateTime.UtcNow;

        if (entries.Count > 0)
        {
            _logger.LogInformation("EventLog '{LogName}': {Count} new error/warning entries",
                logName, entries.Count);
        }

        return entries;
    }

    private static string TruncateMessage(string? message, int maxLength)
    {
        if (string.IsNullOrEmpty(message)) return "";
        return message.Length <= maxLength ? message : message[..maxLength] + "...";
    }
}
