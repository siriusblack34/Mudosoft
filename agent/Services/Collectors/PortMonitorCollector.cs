using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Shared.Dtos;
using System.Net.Sockets;
using System.Text.Json;

namespace Orchestra.Agent.Services.Collectors;

/// <summary>
/// Belirtilen TCP portlarına bağlanarak servis erişilebilirliğini kontrol eder.
/// SQL Server (1433), RDP (3389) gibi kritik servislerin durumunu izler.
/// </summary>
public sealed class PortMonitorCollector : ICollector
{
    private readonly PortMonitorConfig _config;
    private readonly ILogger<PortMonitorCollector> _logger;

    public string Name => "PortMonitor";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public PortMonitorCollector(
        IOptions<CollectorsConfig> config,
        ILogger<PortMonitorCollector> logger)
    {
        _config = config.Value.PortMonitor;
        _logger = logger;
    }

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var results = new List<PortCheckResultDto>();
        var hasFailure = false;

        foreach (var entry in _config.Ports)
        {
            var dto = new PortCheckResultDto
            {
                Port = entry.Port,
                ServiceName = entry.ServiceName
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_config.TimeoutMs);

                await client.ConnectAsync("127.0.0.1", entry.Port, cts.Token);
                sw.Stop();

                dto.IsOpen = true;
                dto.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Gerçek iptal, yukarı fırlat
            }
            catch
            {
                sw.Stop();
                dto.IsOpen = false;
                dto.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
                hasFailure = true;
            }

            results.Add(dto);
        }

        var closedPorts = results.Where(r => !r.IsOpen).ToList();
        if (closedPorts.Count > 0)
        {
            _logger.LogWarning("Closed ports: {Ports}",
                string.Join(", ", closedPorts.Select(p => $"{p.ServiceName}:{p.Port}")));
        }

        return new CollectorResult
        {
            CollectorName = Name,
            Success = true,
            Severity = hasFailure ? "Warning" : "Info",
            JsonData = JsonSerializer.Serialize(results)
        };
    }
}
