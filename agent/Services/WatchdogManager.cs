using Microsoft.Extensions.Logging;

namespace Mudosoft.Agent.Services;

public sealed class WatchdogManager : IWatchdogManager
{
    private readonly ILogger<WatchdogManager> _logger;

    public WatchdogManager(ILogger<WatchdogManager> logger)
    {
        _logger = logger;
    }

    public void Start(CancellationToken token)
    {
        _logger.LogInformation("Watchdog started");
    }
}
