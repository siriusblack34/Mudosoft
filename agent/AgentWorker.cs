using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Options;
using Mudosoft.Agent.Services;

namespace Mudosoft.Agent;

public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly AgentOptions _options;
    private readonly IHeartbeatSender _heartbeatSender;
    private readonly ICommandPoller _commandPoller;
    private readonly IWatchdogManager _watchdogManager;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        IOptions<AgentOptions> options,
        IHeartbeatSender heartbeatSender,
        ICommandPoller commandPoller,
        IWatchdogManager watchdogManager)
    {
        _logger = logger;
        _options = options.Value;
        _heartbeatSender = heartbeatSender;
        _commandPoller = commandPoller;
        _watchdogManager = watchdogManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mudosoft Agent starting with DeviceId={DeviceId}", _options.DeviceId);

        // Watchdog’ları arka planda başlat
        _watchdogManager.Start(stoppingToken);

        var heartbeatPeriod = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds);
        var commandPollPeriod = TimeSpan.FromSeconds(_options.CommandPollIntervalSeconds);

        var heartbeatTask = RunPeriodicAsync(
            () => _heartbeatSender.SendHeartbeatAsync(stoppingToken),
            heartbeatPeriod,
            stoppingToken);

        var commandTask = RunPeriodicAsync(
            () => _commandPoller.PollAndExecuteAsync(stoppingToken),
            commandPollPeriod,
            stoppingToken);

        await Task.WhenAll(heartbeatTask, commandTask);
    }

    private static async Task RunPeriodicAsync(
        Func<Task> action,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                // Burada global loglama yapılabilir
                Console.Error.WriteLine(ex);
            }

            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
