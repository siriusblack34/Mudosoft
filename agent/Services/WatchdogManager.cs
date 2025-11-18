using Microsoft.Extensions.Logging;
using Mudosoft.Shared.Dtos;

namespace Mudosoft.Agent.Services;

public sealed class WatchdogManager : IWatchdogManager
{
    private readonly ILogger<WatchdogManager> _logger;
    private readonly IEventPublisher _eventPublisher;

    public WatchdogManager(
        ILogger<WatchdogManager> logger,
        IEventPublisher eventPublisher)
    {
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    public void Start(CancellationToken cancellationToken)
    {
        // Şimdilik sadece iskelet: buraya timer’lar koyacağız
        _ = Task.Run(() => RunAsync(cancellationToken), cancellationToken);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Watchdog manager started.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // TODO: POS servisleri, disk kullanımı, log tarama, vs.
                // Örnek dummy event:
                var evt = new DeviceEventDto
                {
                    DeviceId = Environment.MachineName,
                    EventType = "watchdog_alive",
                    Severity = "info",
                    Details = "Watchdog tick."
                };

                await _eventPublisher.PublishAsync(evt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchdog loop error.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
