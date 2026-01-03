using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Models;
using Mudosoft.Agent.Interfaces;
using System.Diagnostics;
using System.Management;

namespace Mudosoft.Agent.Services
{
    public class TelemetryService : BackgroundService
    {
        private readonly ILogger<TelemetryService> _logger;
        private readonly AgentConfig _config;
        private readonly IDeviceIdentityProvider _identityProvider;
        private HubConnection? _hubConnection;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;

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
                // Init Performance Counters (Windows Only)
                if (OperatingSystem.IsWindows())
                {
                    // Note: PerformanceCounter only works on Windows
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                    _cpuCounter.NextValue(); // First value is always 0
                }
            }
            catch (Exception ex)
            {
                // DEBUG: Write to file since we are in a service
                try 
                {
                    await File.AppendAllTextAsync(@"C:\mudosoft_agent_debug.log", $"{DateTime.Now}: Connection Error: {ex.Message} {Environment.NewLine} {ex.StackTrace} {Environment.NewLine}"); 
                } catch { }

                _logger.LogError(ex, "Error streaming telemetry");
            }

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Initial delay to let Agent register/login
            await Task.Delay(5000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Ensure connection
                    if (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected)
                    {
                        await ConnectToHubAsync();
                    }

                    if (_hubConnection?.State == HubConnectionState.Connected)
                    {
                        var cpu = _cpuCounter?.NextValue() ?? 0;
                        var ramAvailable = _ramCounter?.NextValue() ?? 0;
                        
                        // Get Total RAM for percentage calc (Basic way)
                        var totalRam = GetTotalRamMb();
                        var ramUsage = totalRam > 0 ? ((totalRam - ramAvailable) / totalRam) * 100 : 0;
                        
                        // Disk Usage (C:)
                        var diskUsage = GetDiskUsage();

                        await _hubConnection.InvokeAsync("SendTelemetry", 
                            _identityProvider.GetDeviceId(), 
                            Math.Round(cpu, 1), 
                            Math.Round(ramUsage, 1), 
                            Math.Round(diskUsage, 1),
                            stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    // _logger.LogWarning($"Telemetry error: {ex.Message}");
                    // Wait a bit more on error
                    await Task.Delay(5000, stoppingToken);
                }

                // Wait 3 seconds
                await Task.Delay(3000, stoppingToken);
            }
        }

        private async Task ConnectToHubAsync()
        {
            try
            {
                var deviceId = _identityProvider.GetDeviceId();
                var hubUrl = $"{_config.BackendUrl.TrimEnd('/')}/hubs/dashboard?deviceId={deviceId}";
                // Token logic removed for anonymous access

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl) 
                    .WithAutomaticReconnect()
                    .Build();

                await _hubConnection.StartAsync();
                _logger.LogInformation("✅ Connected to DashboardHub for Telemetry");
            }
            catch (Exception ex)
            {
                 // DEBUG: Write to file
                try 
                {
                    await File.AppendAllTextAsync(@"C:\mudosoft_agent_debug.log", $"{DateTime.Now}: ConnectToHubAsync Error: {ex.Message} {Environment.NewLine} {ex.StackTrace} {Environment.NewLine}"); 
                } catch { }

                _logger.LogWarning($"Could not connect to DashboardHub: {ex.Message}");
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
            catch { }
            return 0;
        }

        private double GetTotalRamMb()
        {
            // Simplified total RAM fetch
            try 
            {
                var gcMemoryInfo = GC.GetGCMemoryInfo();
                var installedMemory = gcMemoryInfo.TotalAvailableMemoryBytes;
                return (double)installedMemory / (1024 * 1024);
            }
            catch { return 16384; } // Default fallback 16GB
        }
    }
}
