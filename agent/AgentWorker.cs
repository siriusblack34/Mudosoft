// siriusblack34/mudosoft/Mudosoft-138a269b679ef64544ce6a0b899393e338ef513e/agent/AgentWorker.cs

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Models;
using Mudosoft.Agent.Services;
using Mudosoft.Agent.Interfaces;
using System;
using System.Threading.Tasks;

namespace Mudosoft.Agent;

public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly AgentConfig _config;
    private readonly IHeartbeatSender _heartbeatSender;
    private readonly ICommandPoller _commandPoller;
    private readonly IWatchdogManager _watchdogManager;
    private readonly IDeviceIdentityProvider _identityProvider;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        IOptions<AgentConfig> config,
        IHeartbeatSender heartbeatSender,
        ICommandPoller commandPoller,
        IWatchdogManager watchdogManager,
        IDeviceIdentityProvider identityProvider)
    {
        _logger = logger;
        _config = config.Value;
        _heartbeatSender = heartbeatSender;
        _commandPoller = commandPoller;
        _watchdogManager = watchdogManager;
        _identityProvider = identityProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 🏆 KRİTİK DÜZELTME: DeviceId artık kalıcı IdentityProvider'dan alınır ve loglanır.
        string deviceId = _identityProvider.GetDeviceId();
        _logger.LogInformation("Mudosoft Agent starting with DeviceId={DeviceId}", deviceId);

        // Watchdog'ları arka planda başlat
        _watchdogManager.Start(stoppingToken);

        // ✅ DÜZELTME: AgentConfig'teki int tiplerini kullanarak TimeSpan oluştur
        // Emergency hotfix:
        // Store machines were overloaded by aggressive polling defaults.
        // Clamp to safer minimums even if an old config still requests faster loops.
        var heartbeatSeconds = Math.Max(_config.HeartbeatIntervalSeconds, 15);
        var commandPollSeconds = Math.Max(_config.CommandPollIntervalSeconds, 5);

        var heartbeatPeriod = TimeSpan.FromSeconds(heartbeatSeconds);
        var commandPollPeriod = TimeSpan.FromSeconds(commandPollSeconds);

        _logger.LogInformation(
            "Effective intervals applied. Heartbeat={HeartbeatSeconds}s CommandPoll={CommandPollSeconds}s",
            heartbeatSeconds,
            commandPollSeconds);

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

    private async Task RunPeriodicAsync(
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
                _logger.LogError(ex, "Periyodik görev yürütülürken hata oluştu.");
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
