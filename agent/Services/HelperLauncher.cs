using Microsoft.Extensions.Hosting;

namespace Mudosoft.Agent.Services;

/// <summary>
/// HelperLauncher - DISABLED
/// RDHelper now runs via Scheduled Task and connects directly to Hub.
/// This service is kept as a placeholder but does nothing.
/// </summary>
public class HelperLauncher : BackgroundService
{
    private readonly ILogger<HelperLauncher> _logger;
    
    public HelperLauncher(ILogger<HelperLauncher> logger, IConfiguration config, Interfaces.IDeviceIdentityProvider identityProvider)
    {
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HelperLauncher: DISABLED - RDHelper runs via Scheduled Task");
        
        // Do nothing - RDHelper runs independently via scheduled task
        await Task.CompletedTask;
    }
}
