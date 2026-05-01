using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using System.Diagnostics;

namespace Orchestra.Agent.Services
{
    public class TelemetryService : BackgroundService
    {
        private readonly ILogger<TelemetryService> _logger;
        private readonly AgentConfig _config;
        private readonly IDeviceIdentityProvider _identityProvider;
        private HubConnection? _hubConnection;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private TimeSpan _retryDelay = TimeSpan.FromSeconds(15);
        private DateTime _lastFailureLogUtc = DateTime.MinValue;
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

        public TelemetryService(
            ILogger<TelemetryService> logger,
            IOptions<AgentConfig> config,
            IDeviceIdentityProvider identityProvider)
        {
            _logger = logger;
            _config = config.Value;
            _identityProvider = identityProvider;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                    _cpuCounter.NextValue();
                }
            }
            catch (Exception ex)
            {
                WriteDebugLine($"Telemetry counter init error: {ex.Message}");
                _logger.LogError(ex, "Error streaming telemetry");
            }

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(5000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected)
                    {
                        var connected = await ConnectToHubAsync(stoppingToken);
                        if (!connected)
                        {
                            await Task.Delay(_retryDelay, stoppingToken);
                            continue;
                        }
                    }

                    if (_hubConnection?.State == HubConnectionState.Connected)
                    {
                        var cpu = _cpuCounter?.NextValue() ?? 0;
                        var ramAvailable = _ramCounter?.NextValue() ?? 0;
                        var totalRam = GetTotalRamMb();
                        var ramUsage = totalRam > 0 ? ((totalRam - ramAvailable) / totalRam) * 100 : 0;
                        var diskUsage = GetDiskUsage();

                        await _hubConnection.InvokeAsync(
                            "SendTelemetry",
                            _identityProvider.GetDeviceId(),
                            Math.Round(cpu, 1),
                            Math.Round(ramUsage, 1),
                            Math.Round(diskUsage, 1),
                            stoppingToken);

                        _retryDelay = TimeSpan.FromSeconds(15);
                    }
                }
                catch (Exception ex)
                {
                    LogFailure($"Telemetry loop error: {ex.Message}");
                    await Task.Delay(_retryDelay, stoppingToken);
                }

                await Task.Delay(15000, stoppingToken);
            }
        }

        private async Task<bool> ConnectToHubAsync(CancellationToken ct)
        {
            try
            {
                var deviceId = _identityProvider.GetDeviceId();
                var hubUrl = $"{_config.BackendUrl.TrimEnd('/')}/hubs/dashboard?deviceId={deviceId}";

                if (_hubConnection != null)
                {
                    try { await _hubConnection.DisposeAsync(); } catch { }
                    _hubConnection = null;
                }

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl)
                    .WithAutomaticReconnect()
                    .Build();

                await _hubConnection.StartAsync(ct);
                _retryDelay = TimeSpan.FromSeconds(15);
                _logger.LogInformation("Connected to DashboardHub for telemetry");
                return true;
            }
            catch (Exception ex)
            {
                LogFailure($"ConnectToHubAsync error: {ex.Message}");
                return false;
            }
        }

        private void LogFailure(string message)
        {
            var nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastFailureLogUtc >= TimeSpan.FromMinutes(5))
            {
                _lastFailureLogUtc = nowUtc;
                WriteDebugLine(message);
                _logger.LogWarning("{Message}", message);
            }

            var nextSeconds = Math.Min(_retryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds);
            _retryDelay = TimeSpan.FromSeconds(nextSeconds);
        }

        private static void WriteDebugLine(string message)
        {
            try
            {
                File.AppendAllText(
                    @"C:\mudosoft_agent_debug.log",
                    $"{DateTime.Now:dd.MM.yyyy HH:mm:ss}: {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private double GetDiskUsage()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var drive = new DriveInfo("C");
                    if (drive.IsReady)
                    {
                        var used = drive.TotalSize - drive.TotalFreeSpace;
                        return (double)used / drive.TotalSize * 100;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private double GetTotalRamMb()
        {
            try
            {
                var gcMemoryInfo = GC.GetGCMemoryInfo();
                var installedMemory = gcMemoryInfo.TotalAvailableMemoryBytes;
                return (double)installedMemory / (1024 * 1024);
            }
            catch
            {
                return 16384;
            }
        }
    }
}
