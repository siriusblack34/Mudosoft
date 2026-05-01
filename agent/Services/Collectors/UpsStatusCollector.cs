using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Shared.Dtos;
using System.Management;
using System.Text.Json;

namespace Orchestra.Agent.Services.Collectors;

/// <summary>
/// UPS durumunu WMI Win32_Battery üzerinden izler.
/// Pil bilgisi yoksa (masaüstü, UPS yok) "NoBattery" döner.
/// Win7 + Win11 uyumlu.
/// </summary>
public sealed class UpsStatusCollector : ICollector
{
    private readonly UpsStatusConfig _config;
    private readonly ILogger<UpsStatusCollector> _logger;
    private bool _hasBattery = true; // İlk denemede batarya yoksa false

    public string Name => "UpsStatus";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public UpsStatusCollector(
        IOptions<CollectorsConfig> config,
        ILogger<UpsStatusCollector> logger)
    {
        _config = config.Value.UpsStatus;
        _logger = logger;
    }

    public Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        if (!_hasBattery)
        {
            return Task.FromResult(new CollectorResult
            {
                CollectorName = Name,
                Success = true,
                Severity = "Info",
                JsonData = JsonSerializer.Serialize(new UpsStatusDto
                {
                    Name = "N/A",
                    Status = "NoBattery",
                    Source = "WMI"
                })
            });
        }

        var result = ReadFromWmi();
        return Task.FromResult(result);
    }

    private CollectorResult ReadFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, BatteryStatus, EstimatedChargeRemaining, EstimatedRunTime FROM Win32_Battery");

            var batteries = searcher.Get();
            if (batteries.Count == 0)
            {
                _hasBattery = false;
                _logger.LogInformation("No battery/UPS detected via WMI");
                return new CollectorResult
                {
                    CollectorName = Name,
                    Success = true,
                    Severity = "Info",
                    JsonData = JsonSerializer.Serialize(new UpsStatusDto
                    {
                        Name = "N/A",
                        Status = "NoBattery",
                        Source = "WMI"
                    })
                };
            }

            var results = new List<UpsStatusDto>();
            var hasCritical = false;

            foreach (var obj in batteries)
            {
                var name = obj["Name"]?.ToString() ?? "Battery";
                var batteryStatus = Convert.ToInt32(obj["BatteryStatus"] ?? 0);
                var chargePercent = Convert.ToInt32(obj["EstimatedChargeRemaining"] ?? 0);
                var runtimeMin = Convert.ToInt32(obj["EstimatedRunTime"] ?? 0);

                // BatteryStatus: 1=Discharging, 2=AC/Online, 3=FullyCharged, 4=Low, 5=Critical
                var status = batteryStatus switch
                {
                    1 => "OnBattery",
                    2 => "Online",
                    3 => "FullyCharged",
                    4 => "LowBattery",
                    5 => "Critical",
                    _ => "Unknown"
                };

                if (status is "LowBattery" or "Critical" or "OnBattery")
                {
                    hasCritical = true;
                    _logger.LogWarning("UPS/Battery '{Name}': {Status}, {Pct}%, ~{Min}min",
                        name, status, chargePercent, runtimeMin);
                }

                results.Add(new UpsStatusDto
                {
                    Name = name,
                    Status = status,
                    BatteryPercent = chargePercent,
                    EstimatedRuntimeMinutes = runtimeMin > 0 ? runtimeMin : null,
                    Source = "WMI"
                });
            }

            return new CollectorResult
            {
                CollectorName = Name,
                Success = true,
                Severity = hasCritical ? "Critical" : "Info",
                JsonData = JsonSerializer.Serialize(results.Count == 1 ? (object)results[0] : results)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UPS WMI query error");
            return new CollectorResult
            {
                CollectorName = Name,
                Success = false,
                Severity = "Warning",
                ErrorMessage = ex.Message,
                JsonData = "{}"
            };
        }
    }
}
