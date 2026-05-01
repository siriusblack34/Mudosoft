using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Shared.Dtos;
using System.Management;
using System.Text.Json;

namespace Orchestra.Agent.Services.Collectors;

/// <summary>
/// WMI MSAcpi_ThermalZoneTemperature ile sıcaklık verisi toplar.
/// Not: Çoğu masaüstü/POS sistemi bu WMI sınıfını desteklemez.
/// Desteklenmiyorsa "NotAvailable" döner ve sessizce devam eder.
/// Win7 + Win11 uyumlu.
/// </summary>
public sealed class TemperatureCollector : ICollector
{
    private readonly TemperatureConfig _config;
    private readonly ILogger<TemperatureCollector> _logger;
    private bool _wmiAvailable = true; // İlk denemede WMI yoksa false yapıp gereksiz sorguları önle

    public string Name => "Temperature";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public TemperatureCollector(
        IOptions<CollectorsConfig> config,
        ILogger<TemperatureCollector> logger)
    {
        _config = config.Value.Temperature;
        _logger = logger;
    }

    public Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var readings = new List<TemperatureReadingDto>();

        if (_wmiAvailable)
        {
            readings = ReadFromWmi();
        }

        if (readings.Count == 0)
        {
            return Task.FromResult(new CollectorResult
            {
                CollectorName = Name,
                Success = true,
                Severity = "Info",
                JsonData = JsonSerializer.Serialize(new[]
                {
                    new TemperatureReadingDto
                    {
                        SensorName = "N/A",
                        TemperatureCelsius = 0,
                        Status = "NotAvailable"
                    }
                })
            });
        }

        var hasCritical = readings.Any(r => r.Status == "Critical");
        var hasWarning = readings.Any(r => r.Status == "Warning");

        return Task.FromResult(new CollectorResult
        {
            CollectorName = Name,
            Success = true,
            Severity = hasCritical ? "Critical" : hasWarning ? "Warning" : "Info",
            JsonData = JsonSerializer.Serialize(readings)
        });
    }

    private List<TemperatureReadingDto> ReadFromWmi()
    {
        var results = new List<TemperatureReadingDto>();

        try
        {
            // MSAcpi_ThermalZoneTemperature - root\WMI namespace
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            int index = 0;
            foreach (var obj in searcher.Get())
            {
                var instanceName = obj["InstanceName"]?.ToString() ?? $"Zone_{index}";
                var tempKelvin = Convert.ToDouble(obj["CurrentTemperature"]);

                // WMI sıcaklığı deciKelvin cinsinden verir (örn: 3132 = 313.2K = 40.05°C)
                var celsius = (tempKelvin / 10.0) - 273.15;

                var status = celsius switch
                {
                    >= 90 => "Critical",
                    >= 75 => "Warning",
                    _ => "Normal"
                };

                if (status == "Critical")
                    _logger.LogWarning("Temperature CRITICAL: {Sensor} = {Temp:F1}°C", instanceName, celsius);

                results.Add(new TemperatureReadingDto
                {
                    SensorName = SimplifySensorName(instanceName, index),
                    TemperatureCelsius = Math.Round(celsius, 1),
                    Status = status
                });
                index++;
            }
        }
        catch (ManagementException ex)
        {
            // WMI sınıfı bu makinede mevcut değil
            _wmiAvailable = false;
            _logger.LogInformation("Temperature WMI not available: {Msg}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Temperature read error");
        }

        return results;
    }

    private static string SimplifySensorName(string instanceName, int index)
    {
        if (instanceName.Contains("ThermalZone", StringComparison.OrdinalIgnoreCase))
        {
            return index switch
            {
                0 => "CPU",
                1 => "Anakart",
                _ => $"Thermal Zone {index}"
            };
        }
        return instanceName.Length > 30 ? instanceName[..30] : instanceName;
    }
}
