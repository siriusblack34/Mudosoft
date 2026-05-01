using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using Orchestra.Shared.Dtos;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace Orchestra.Agent.Services.Collectors;

/// <summary>
/// Basit ağ hız testi: bir test dosyası indirerek download hızını ölçer.
/// Ping ile latency ölçer. Upload testi opsiyonel (çoğu test sunucusu desteklemez).
/// Win7 + Win11 uyumlu - sadece HttpClient ve Ping kullanır.
/// </summary>
public sealed class NetworkSpeedCollector : ICollector
{
    private readonly NetworkSpeedConfig _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<NetworkSpeedCollector> _logger;

    public string Name => "NetworkSpeed";
    public TimeSpan Interval => TimeSpan.FromSeconds(_config.IntervalSeconds);
    public bool Enabled => _config.Enabled;

    public NetworkSpeedCollector(
        IOptions<CollectorsConfig> config,
        IHttpClientFactory httpFactory,
        ILogger<NetworkSpeedCollector> logger)
    {
        _config = config.Value.NetworkSpeed;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var result = new NetworkSpeedDto
        {
            TestedAt = DateTime.UtcNow,
            TestServer = _config.TestUrl
        };

        // 1. Latency testi (ping)
        result.LatencyMs = await MeasureLatency(ct);

        // 2. Download hız testi
        result.DownloadMbps = await MeasureDownload(ct);

        var severity = "Info";
        if (result.DownloadMbps < 1.0 && result.DownloadMbps > 0)
        {
            severity = "Warning";
            _logger.LogWarning("Network speed low: {Speed:F1} Mbps", result.DownloadMbps);
        }

        return new CollectorResult
        {
            CollectorName = Name,
            Success = true,
            Severity = severity,
            JsonData = JsonSerializer.Serialize(result)
        };
    }

    private async Task<int> MeasureLatency(CancellationToken ct)
    {
        try
        {
            // Test URL'den host çıkar
            var uri = new Uri(_config.TestUrl);
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(uri.Host, 5000);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ping failed");
            return -1;
        }
    }

    private async Task<double> MeasureDownload(CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);

            var sw = Stopwatch.StartNew();
            using var response = await client.GetAsync(_config.TestUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = 0;
            var buffer = new byte[8192];
            using var stream = await response.Content.ReadAsStreamAsync(ct);

            while (true)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;
                totalBytes += read;

                // Timeout kontrolü (max 15 saniye download)
                if (sw.Elapsed.TotalSeconds > 15) break;
            }

            sw.Stop();

            if (totalBytes == 0 || sw.ElapsedMilliseconds == 0) return 0;

            // bytes -> bits -> megabits, seconds
            var megabits = (totalBytes * 8.0) / (1024.0 * 1024.0);
            var seconds = sw.ElapsedMilliseconds / 1000.0;
            var mbps = megabits / seconds;

            _logger.LogInformation("Network speed: {Speed:F1} Mbps ({Bytes} bytes in {Sec:F1}s)",
                mbps, totalBytes, seconds);

            return Math.Round(mbps, 2);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Download speed test failed");
            return 0;
        }
    }
}
