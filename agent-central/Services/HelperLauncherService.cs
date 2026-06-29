using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Orchestra.CentralAgent.Services;

/// <summary>
/// Windows Servisi içinde (Session 0) çalışır.
/// Kullanıcı oturumunda --helper sürecinin çalışmasını garanti eder.
/// Her 30 saniyede bir kontrol eder; süreç ölmüşse yeniden başlatır.
/// </summary>
[SupportedOSPlatform("windows")]
public class HelperLauncherService : BackgroundService
{
    private readonly ILogger<HelperLauncherService> _logger;

    public HelperLauncherService(ILogger<HelperLauncherService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Servis stabilize olsun
        await Task.Delay(5_000, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            EnsureHelperRunning();
            await Task.Delay(30_000, stoppingToken).ConfigureAwait(false);
        }
    }

    private void EnsureHelperRunning()
    {
        try
        {
            var currentPid = Environment.ProcessId;

            // Session 0 dışındaki (kullanıcı oturumu) process'leri bul
            var helpers = Process.GetProcessesByName("OrchestraCentralAgent")
                .Where(p => p.Id != currentPid && p.SessionId > 0)
                .OrderBy(p => p.StartTime)
                .ToArray();

            if (helpers.Length > 1)
            {
                // Birden fazla helper var — eskilerini öldür, en yenisini bırak
                foreach (var dup in helpers.SkipLast(1))
                {
                    _logger.LogWarning("Fazla helper PID {Pid} öldürülüyor", dup.Id);
                    try { dup.Kill(); } catch { }
                }
                return;
            }

            if (helpers.Length == 1)
            {
                _logger.LogDebug("Helper zaten çalışıyor (PID {Pid})", helpers[0].Id);
                return;
            }

            var exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? string.Empty;

            if (string.IsNullOrEmpty(exePath))
            {
                _logger.LogWarning("EXE yolu bulunamadı, helper başlatılamıyor");
                return;
            }

            bool ok = UserSessionLauncher.LaunchInUserSession(exePath, "--helper");
            _logger.LogInformation(ok
                ? "Helper süreci kullanıcı oturumuna başlatıldı"
                : "Helper başlatılamadı (aktif kullanıcı oturumu yok?)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("HelperLauncherService hata: {Msg}", ex.Message);
        }
    }
}
