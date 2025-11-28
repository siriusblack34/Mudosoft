using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Models;
using Mudosoft.Agent.Services;
using Mudosoft.Agent.Interfaces; // ⬅️ Yeni using

namespace Mudosoft.Agent;

public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly AgentConfig _config;
    private readonly IHeartbeatSender _heartbeatSender;
    private readonly ICommandPoller _commandPoller;
    private readonly IWatchdogManager _watchdogManager;
    private readonly IDeviceIdentityProvider _identityProvider; // ⬅️ Yeni

    public AgentWorker(
        ILogger<AgentWorker> logger,
        IOptions<AgentConfig> config,
        IHeartbeatSender heartbeatSender,
        ICommandPoller commandPoller,
        IWatchdogManager watchdogManager,
        IDeviceIdentityProvider identityProvider) // ⬅️ Yeni Enjeksiyon
    {
        _logger = logger;
        _config = config.Value;
        _heartbeatSender = heartbeatSender;
        _commandPoller = commandPoller;
        _watchdogManager = watchdogManager;
        _identityProvider = identityProvider; // ⬅️ Yeni
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 🏆 KRİTİK DÜZELTME: DeviceId artık kalıcı IdentityProvider'dan alınır ve loglanır.
        string deviceId = _identityProvider.GetDeviceId();
        _logger.LogInformation("Mudosoft Agent starting with DeviceId={DeviceId}", deviceId);

        // Watchdog’ları arka planda başlat
        _watchdogManager.Start(stoppingToken);

        var heartbeatPeriod = TimeSpan.FromSeconds(_config.HeartbeatIntervalSeconds);
        var commandPollPeriod = TimeSpan.FromSeconds(_config.CommandPollIntervalSeconds);

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
                // 🔥 KRİTİK DÜZELTME: Console.Error.WriteLine yerine ILogger kullanılıyor.
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