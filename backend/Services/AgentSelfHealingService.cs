using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services;

/// <summary>
/// Offline düşen bir cihazda WMI ile MudosoftAgentService'i yeniden başlatmayı dener.
/// HeartbeatCheckerWorker tarafından cihaz offline olarak işaretlendiğinde çağrılır.
/// Saha PC'lerinde WSMan (5985) kapalı, DCOM (135) açık olduğu için wmic /node: kullanılır.
/// </summary>
public class AgentSelfHealingService
{
    private const string AgentServiceName = "MudosoftAgentService";
    private const int MaxRetries = 3;
    // Aynı cihaz için son X dakika içinde tekrar deneme
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> _state = new();
    private readonly ILogger<AgentSelfHealingService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public AgentSelfHealingService(ILogger<AgentSelfHealingService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Bir cihaz offline olduğunda tetiklenir. Başarılıysa agent kendi kendine yeniden bağlanır.
    /// </summary>
    public async Task TryHealAsync(string deviceId, string? hostname, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) && string.IsNullOrWhiteSpace(hostname))
        {
            _logger.LogWarning("SelfHeal: Cihaz {DeviceId} için IP/hostname yok, atlandı.", deviceId);
            return;
        }

        // Self-healing etkin mi kontrol et
        if (!await IsSelfHealingEnabledAsync()) return;

        // Retry ve cooldown kontrolü
        var key = deviceId;
        var now = DateTime.UtcNow;

        if (_state.TryGetValue(key, out var s))
        {
            if (s.attempts >= MaxRetries)
            {
                _logger.LogWarning("SelfHeal: Cihaz {Hostname} ({DeviceId}) maksimum deneme sayısına ({Max}) ulaştı, durdu.", hostname, deviceId, MaxRetries);
                return;
            }

            if (now - s.lastAttempt < RetryCooldown)
            {
                _logger.LogDebug("SelfHeal: Cihaz {Hostname} için henüz cooldown süresi dolmadı.", hostname);
                return;
            }
        }

        var target = !string.IsNullOrWhiteSpace(ipAddress) ? ipAddress : hostname!;
        _logger.LogInformation("SelfHeal: Cihaz {Hostname} ({Target}) için agent yeniden başlatma deneniyor...", hostname, target);

        var success = await RestartAgentServiceAsync(target);

        var newAttempts = (s.attempts) + 1;
        _state[key] = (newAttempts, now);

        await LogHealAttemptAsync(deviceId, hostname, target, success, newAttempts);

        if (success)
        {
            _logger.LogInformation("SelfHeal: {Hostname} agent servisi başarıyla yeniden başlatıldı (deneme #{Attempt})", hostname, newAttempts);
            // Başarılı olursa sayacı sıfırla
            _state.TryRemove(key, out _);
        }
        else
        {
            _logger.LogWarning("SelfHeal: {Hostname} agent servisi yeniden başlatılamadı (deneme #{Attempt}/{Max})", hostname, newAttempts, MaxRetries);
        }
    }

    /// <summary>
    /// Cihaz tekrar online geldiğinde sayacı sıfırla.
    /// </summary>
    public void ResetDevice(string deviceId)
    {
        _state.TryRemove(deviceId, out _);
    }

    private async Task<bool> RestartAgentServiceAsync(string target)
    {
        var wmiUser = Environment.GetEnvironmentVariable("WMI_USER") ?? "MUDODMN\\mudoadmtd";
        var wmiPass = Environment.GetEnvironmentVariable("WMI_PASSWORD") ?? "";

        // Önce stop dene (servis takılı kalmış olabilir)
        var stopArgs = $@"/node:""{target}"" /user:""{wmiUser}"" /password:""{wmiPass}"" service where ""name='{AgentServiceName}'"" call stopservice";
        await RunWmicAsync(stopArgs, timeoutSeconds: 20);

        // 2 saniye bekle
        await Task.Delay(2000);

        // Start
        var startArgs = $@"/node:""{target}"" /user:""{wmiUser}"" /password:""{wmiPass}"" service where ""name='{AgentServiceName}'"" call startservice";
        var result = await RunWmicAsync(startArgs, timeoutSeconds: 30);

        return result.exitCode == 0 && result.output.Contains("ReturnValue = 0");
    }

    private static async Task<(int exitCode, string output)> RunWmicAsync(string args, int timeoutSeconds)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            proc.Start();
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            var exited = await Task.Run(() => proc.WaitForExit(timeoutSeconds * 1000));
            if (!exited) { proc.Kill(); return (-1, "Timeout"); }

            var output = await outTask + await errTask;
            return (proc.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private async Task<bool> IsSelfHealingEnabledAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var setting = await db.AppSettings.FindAsync("selfhealing:enabled");
            if (setting == null) return true; // Varsayılan: etkin
            return setting.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch { return true; }
    }

    private async Task LogHealAttemptAsync(string deviceId, string? hostname, string target, bool success, int attempt)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            db.ActivityLogs.Add(new ActivityLog
            {
                Username = "System",
                Category = "SelfHealing",
                Action = success ? "HealSuccess" : "HealFailed",
                Target = hostname ?? target,
                Details = $"Deneme #{attempt}",
                Success = success,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }
        catch { /* loglama hatası kritik değil */ }
    }
}
