using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mudosoft.Agent.Interfaces;

namespace Mudosoft.Agent.Services.Collectors;

/// <summary>
/// Tüm ICollector implementasyonlarını yöneten BackgroundService.
/// Her collector kendi interval'ında bağımsız çalışır.
/// Bir collector çökse diğerlerini etkilemez (fault isolation).
/// </summary>
public sealed class CollectorOrchestrator : BackgroundService
{
    private readonly IEnumerable<ICollector> _collectors;
    private readonly CollectorReportSender _sender;
    private readonly ILogger<CollectorOrchestrator> _logger;

    public CollectorOrchestrator(
        IEnumerable<ICollector> collectors,
        CollectorReportSender sender,
        ILogger<CollectorOrchestrator> logger)
    {
        _collectors = collectors;
        _sender = sender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var activeCollectors = _collectors.Where(c => c.Enabled).ToList();

        if (activeCollectors.Count == 0)
        {
            _logger.LogWarning("No enabled collectors found. CollectorOrchestrator idle.");
            return;
        }

        _logger.LogInformation("CollectorOrchestrator starting with {Count} collectors: {Names}",
            activeCollectors.Count,
            string.Join(", ", activeCollectors.Select(c => c.Name)));

        // Her collector için ayrı bir Task başlat
        var tasks = activeCollectors.Select(c => RunCollectorLoop(c, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task RunCollectorLoop(ICollector collector, CancellationToken ct)
    {
        _logger.LogInformation("Collector '{Name}' started (interval: {Interval}s)",
            collector.Name, collector.Interval.TotalSeconds);

        // İlk çalıştırmayı biraz geciktir (collector'lar aynı anda patlamasın)
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(1, 5)), ct);

        while (!ct.IsCancellationRequested)
        {
            CollectorResult? result = null;
            try
            {
                result = await collector.CollectAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Collector '{Name}' threw an unhandled exception", collector.Name);
                result = new CollectorResult
                {
                    CollectorName = collector.Name,
                    Success = false,
                    Severity = "Warning",
                    ErrorMessage = ex.Message
                };
            }

            // Sonucu hemen gönder
            if (result != null)
            {
                try
                {
                    await _sender.SendAsync(new[] { result }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send result for collector '{Name}'", collector.Name);
                }
            }

            // Interval bekle
            try
            {
                await Task.Delay(collector.Interval, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Collector '{Name}' stopped", collector.Name);
    }
}
