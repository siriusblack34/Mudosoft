// siriusblack34/mudosoft/Mudosoft-138a269b679ef64544ce6a0b899393e338ef513e/agent/AgentWorker.cs

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Models;
using Orchestra.Agent.Services;
using Orchestra.Agent.Interfaces;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Orchestra.Agent;

public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly AgentConfig _config;
    private readonly IHeartbeatSender _heartbeatSender;
    private readonly ICommandPoller _commandPoller;
    private readonly IWatchdogManager _watchdogManager;
    private readonly IDeviceIdentityProvider _identityProvider;

    private static readonly string DiagLogPath = Path.Combine(AppContext.BaseDirectory, "mudosoft_helper.log");
    private static void DiagLog(string msg)
    {
        try { File.AppendAllText(DiagLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: [AgentWorker] {msg}{Environment.NewLine}"); } catch { }
    }

    public AgentWorker(
        ILogger<AgentWorker> logger,
        IOptions<AgentConfig> config,
        IHeartbeatSender heartbeatSender,
        ICommandPoller commandPoller,
        IWatchdogManager watchdogManager,
        IDeviceIdentityProvider identityProvider)
    {
        DiagLog("ctor begin");
        _logger = logger;
        _config = config.Value;
        _heartbeatSender = heartbeatSender;
        _commandPoller = commandPoller;
        _watchdogManager = watchdogManager;
        _identityProvider = identityProvider;
        DiagLog("ctor end");
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        DiagLog("StartAsync begin");
        var t = base.StartAsync(cancellationToken);
        DiagLog("StartAsync returning to host (ExecuteAsync running async)");
        return t;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DiagLog("ExecuteAsync begin");
        // 🏆 KRİTİK DÜZELTME: DeviceId artık kalıcı IdentityProvider'dan alınır ve loglanır.
        string deviceId = _identityProvider.GetDeviceId();
        DiagLog($"deviceId resolved: {deviceId}");
        _logger.LogInformation("Mudosoft Agent starting with DeviceId={DeviceId}", deviceId);

        // Watchdog'ları arka planda başlat
        _watchdogManager.Start(stoppingToken);
        DiagLog("watchdog started");

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

        DiagLog("periodic tasks scheduled, awaiting WhenAll");
        await Task.WhenAll(heartbeatTask, commandTask);
        DiagLog("ExecuteAsync exiting");
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
