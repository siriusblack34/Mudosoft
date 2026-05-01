using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Shared.Dtos;
using System.Diagnostics;
using System.Text.Json;

namespace Orchestra.Agent.Services.Collectors;

/// <summary>
/// En çok CPU ve RAM kullanan süreçleri toplar.
/// Win7 ve Win11 uyumlu - sadece System.Diagnostics.Process kullanır.
/// </summary>
public sealed class ProcessUsageCollector : ICollector
{
    private readonly ProcessUsageConfig _config;
    private readonly ILogger<ProcessUsageCollector> _logger;

    public string Name => "ProcessUsage";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public ProcessUsageCollector(
        IOptions<CollectorsConfig> config,
        ILogger<ProcessUsageCollector> logger)
    {
        _config = config.Value.ProcessUsage;
        _logger = logger;
    }

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        // İki snapshot alarak CPU kullanımını hesapla
        var snapshot1 = GetProcessSnapshot();
        await Task.Delay(1000, ct); // 1 saniye bekle
        var snapshot2 = GetProcessSnapshot();

        var processorCount = Environment.ProcessorCount;
        var results = new List<TopProcessDto>();

        foreach (var (pid, info2) in snapshot2)
        {
            if (!snapshot1.TryGetValue(pid, out var info1)) continue;

            var cpuTimeDelta = (info2.CpuTime - info1.CpuTime).TotalMilliseconds;
            var cpuPercent = (cpuTimeDelta / 1000.0) / processorCount * 100.0;

            results.Add(new TopProcessDto
            {
                Name = info2.Name,
                Pid = pid,
                CpuPercent = Math.Round(cpuPercent, 1),
                RamMB = Math.Round(info2.RamBytes / (1024.0 * 1024.0), 1)
            });
        }

        // En çok CPU kullananları al
        var topByCpu = results
            .OrderByDescending(p => p.CpuPercent)
            .Take(_config.TopCount)
            .ToList();

        return new CollectorResult
        {
            CollectorName = Name,
            Success = true,
            Severity = "Info",
            JsonData = JsonSerializer.Serialize(topByCpu)
        };
    }

    private Dictionary<int, ProcessInfo> GetProcessSnapshot()
    {
        var dict = new Dictionary<int, ProcessInfo>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                dict[proc.Id] = new ProcessInfo
                {
                    Name = proc.ProcessName,
                    CpuTime = proc.TotalProcessorTime,
                    RamBytes = proc.WorkingSet64
                };
            }
            catch
            {
                // Erişim izni olmayan süreçleri atla (System, Idle vb.)
            }
            finally
            {
                proc.Dispose();
            }
        }
        return dict;
    }

    private record ProcessInfo
    {
        public string Name { get; init; } = "";
        public TimeSpan CpuTime { get; init; }
        public long RamBytes { get; init; }
    }
}
