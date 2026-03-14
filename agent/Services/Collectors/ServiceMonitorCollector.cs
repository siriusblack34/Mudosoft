using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Interfaces;
using Mudosoft.Agent.Models;
using Mudosoft.Shared.Dtos;
using System.ServiceProcess;
using System.Text.Json;

namespace Mudosoft.Agent.Services.Collectors;

/// <summary>
/// Belirtilen Windows servislerinin durumunu izler.
/// Servis durmuşsa ve AutoRestart açıksa otomatik yeniden başlatır.
/// Saat başına MaxRestartsPerHour ile restart flood'unu önler.
/// </summary>
public sealed class ServiceMonitorCollector : ICollector
{
    private readonly ServiceMonitorConfig _config;
    private readonly ILogger<ServiceMonitorCollector> _logger;

    // Restart sayaçları: ServiceName -> (restart count, window start)
    private readonly Dictionary<string, (int Count, DateTime WindowStart)> _restartCounters = new();

    public string Name => "ServiceMonitor";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public ServiceMonitorCollector(
        IOptions<CollectorsConfig> config,
        ILogger<ServiceMonitorCollector> logger)
    {
        _config = config.Value.ServiceMonitor;
        _logger = logger;
    }

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var results = new List<ServiceStatusDto>();
        var hasCritical = false;

        foreach (var serviceName in _config.MonitoredServices)
        {
            var dto = new ServiceStatusDto { ServiceName = serviceName };

            try
            {
                using var sc = new ServiceController(serviceName);
                dto.DisplayName = sc.DisplayName;
                dto.Status = sc.Status.ToString();

                if (sc.Status != ServiceControllerStatus.Running)
                {
                    _logger.LogWarning("Service '{Name}' is {Status}", serviceName, sc.Status);
                    hasCritical = true;

                    // AutoRestart denemesi
                    if (_config.AutoRestart && sc.Status == ServiceControllerStatus.Stopped)
                    {
                        dto.ActionTaken = await TryRestartAsync(sc, serviceName, ct);
                    }
                }
            }
            catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception)
            {
                // Servis bulunamadı
                dto.Status = "NotFound";
                dto.ErrorMessage = $"Service '{serviceName}' not found on this machine";
                _logger.LogWarning("Service '{Name}' not found", serviceName);
            }
            catch (Exception ex)
            {
                dto.Status = "Error";
                dto.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error checking service '{Name}'", serviceName);
            }

            results.Add(dto);
        }

        return new CollectorResult
        {
            CollectorName = Name,
            Success = true,
            Severity = hasCritical ? "Critical" : "Info",
            JsonData = JsonSerializer.Serialize(results)
        };
    }

    private async Task<string> TryRestartAsync(ServiceController sc, string serviceName, CancellationToken ct)
    {
        // Rate limit kontrolü
        if (!CanRestart(serviceName))
        {
            _logger.LogWarning("Restart limit reached for '{Name}' ({Max}/hour)",
                serviceName, _config.MaxRestartsPerHour);
            return "RestartLimitReached";
        }

        try
        {
            _logger.LogInformation("Attempting to restart service '{Name}'...", serviceName);
            sc.Start();

            // 30 saniye bekle
            var timeout = TimeSpan.FromSeconds(30);
            await Task.Run(() => sc.WaitForStatus(ServiceControllerStatus.Running, timeout), ct);

            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Running)
            {
                _logger.LogInformation("Service '{Name}' restarted successfully", serviceName);
                RecordRestart(serviceName);
                return "Restarted";
            }

            _logger.LogWarning("Service '{Name}' restart timeout - status: {Status}", serviceName, sc.Status);
            return "RestartTimeout";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart service '{Name}'", serviceName);
            return "RestartFailed";
        }
    }

    private bool CanRestart(string serviceName)
    {
        if (!_restartCounters.TryGetValue(serviceName, out var counter))
            return true;

        // 1 saatlik pencere geçtiyse sıfırla
        if ((DateTime.UtcNow - counter.WindowStart).TotalHours >= 1)
            return true;

        return counter.Count < _config.MaxRestartsPerHour;
    }

    private void RecordRestart(string serviceName)
    {
        if (!_restartCounters.TryGetValue(serviceName, out var counter) ||
            (DateTime.UtcNow - counter.WindowStart).TotalHours >= 1)
        {
            _restartCounters[serviceName] = (1, DateTime.UtcNow);
        }
        else
        {
            _restartCounters[serviceName] = (counter.Count + 1, counter.WindowStart);
        }
    }
}
