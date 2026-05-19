using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Models;

namespace Orchestra.Agent.Services;

// Periyodik olarak backend'in yayinladigi son agent surumunu kontrol eder.
// Yeni surum mevcutsa CommandExecutor.TriggerSelfUpdate'i cagirir — boylece backend'in
// "trigger-all" basmasini beklemeden agent kendiliginden guncellenir.
public sealed class AgentUpdateCheckerService : BackgroundService
{
    private readonly HttpClient _http;
    private readonly AgentConfig _config;
    private readonly ICommandExecutor _executor;
    private readonly ILogger<AgentUpdateCheckerService> _logger;

    private static readonly string DiagLogPath = Path.Combine(AppContext.BaseDirectory, "mudosoft_helper.log");
    private static void DiagLog(string msg)
    {
        try { File.AppendAllText(DiagLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: [UpdateChecker] {msg}{Environment.NewLine}"); } catch { }
    }

    // Boot stabilize olsun diye ilk kontrole 60 saniye gecikme.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);
    // Saatte bir kontrol yeterli — daha sik gereksiz backend yuku.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    public AgentUpdateCheckerService(
        IHttpClientFactory httpFactory,
        IOptions<AgentConfig> config,
        ICommandExecutor executor,
        ILogger<AgentUpdateCheckerService> logger)
    {
        _http = httpFactory.CreateClient();
        _config = config.Value;
        _executor = executor;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_config.BackendUrl))
        {
            _http.BaseAddress = new Uri(_config.BackendUrl);
        }
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DiagLog($"started, initial delay {(int)InitialDelay.TotalSeconds}s, interval {(int)CheckInterval.TotalMinutes}m");
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                DiagLog($"check failed: {ex.Message}");
                _logger.LogWarning(ex, "Update check sirasinda hata.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckOnceAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(_config.BackendUrl))
        {
            _logger.LogDebug("BackendUrl bos, update check atlandi.");
            return;
        }

        LatestVersionInfo? info;
        try
        {
            info = await _http.GetFromJsonAsync<LatestVersionInfo>("api/updates/latest", token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("latest.json cekilemedi: {Msg}", ex.Message);
            return;
        }

        if (info == null || string.IsNullOrWhiteSpace(info.Version) ||
            info.Version.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Backend yayinlanmis surum yok.");
            return;
        }

        if (!Version.TryParse(info.Version, out var latest))
        {
            _logger.LogWarning("Backend version parse edilemedi: '{Version}'", info.Version);
            return;
        }

        var currentRaw = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        if (string.IsNullOrEmpty(currentRaw) || !Version.TryParse(currentRaw, out var current))
        {
            _logger.LogWarning("Mevcut surum okunamadi.");
            return;
        }

        if (latest <= current)
        {
            DiagLog($"check ok: current={current} latest={latest} (no update)");
            _logger.LogInformation("Surum guncel: current={Current} latest={Latest}", current, latest);
            return;
        }

        DiagLog($"NEW VERSION: current={current} latest={latest} ({info.FileName}) — triggering self-update");
        _logger.LogInformation(
            "Yeni surum mevcut: {Current} -> {Latest} ({FileName}). Self-update tetikleniyor.",
            current, latest, info.FileName ?? "?");
        _executor.TriggerSelfUpdate(_config.BackendUrl);
    }

    private sealed class LatestVersionInfo
    {
        public string? Version { get; set; }
        public string? FileName { get; set; }
        public string? UploadedAt { get; set; }
        public long SizeBytes { get; set; }
    }
}
