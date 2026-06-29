using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services;

public interface IPlaybookEngine
{
    Task ExecuteAsync(RemediationPlaybook playbook, string? deviceId, string? hostname, string? storeCode, string triggerReason);
}

public class PlaybookEngine : IPlaybookEngine
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlaybookEngine> _logger;

    public PlaybookEngine(IServiceScopeFactory scopeFactory, ILogger<PlaybookEngine> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(RemediationPlaybook playbook, string? deviceId, string? hostname, string? storeCode, string triggerReason)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

        var execution = new PlaybookExecution
        {
            PlaybookId = playbook.Id,
            DeviceId = deviceId,
            Hostname = hostname,
            StoreCode = storeCode,
            Status = "Running",
            StartedAt = DateTime.UtcNow,
            TriggerReason = triggerReason
        };

        db.PlaybookExecutions.Add(execution);
        await db.SaveChangesAsync();

        var results = new List<string>();
        bool anyFailed = false;

        try
        {
            var steps = playbook.Steps.OrderBy(s => s.StepOrder).ToList();

            foreach (var step in steps)
            {
                if (step.DelaySeconds > 0)
                {
                    _logger.LogInformation("Playbook {Name} Step {Order}: {Delay}s bekleniyor", playbook.Name, step.StepOrder, step.DelaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(step.DelaySeconds));
                }

                _logger.LogInformation("Playbook {Name} Step {Order} ({ActionType}) çalışıyor. Cihaz: {Hostname}", playbook.Name, step.StepOrder, step.ActionType, hostname ?? "belirtilmedi");

                var (success, message) = await ExecuteStepAsync(step, deviceId, hostname, storeCode, scope.ServiceProvider);
                results.Add($"[{step.StepOrder}] {step.ActionType}: {message}");

                if (!success)
                {
                    anyFailed = true;
                    _logger.LogWarning("Playbook {Name} Step {Order} başarısız: {Message}", playbook.Name, step.StepOrder, message);
                }
            }

            // Execution kaydını güncelle
            var exec = await db.PlaybookExecutions.FindAsync(execution.Id);
            if (exec != null)
            {
                exec.Status = anyFailed ? (results.Any(r => r.Contains("başarılı")) ? "Partial" : "Failed") : "Success";
                exec.CompletedAt = DateTime.UtcNow;
                exec.ResultSummary = string.Join("\n", results);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playbook {Name} çalışırken beklenmeyen hata", playbook.Name);

            var exec = await db.PlaybookExecutions.FindAsync(execution.Id);
            if (exec != null)
            {
                exec.Status = "Failed";
                exec.CompletedAt = DateTime.UtcNow;
                exec.ResultSummary = $"Beklenmeyen hata: {ex.Message}";
                await db.SaveChangesAsync();
            }
        }
    }

    private async Task<(bool success, string message)> ExecuteStepAsync(
        PlaybookStep step, string? deviceId, string? hostname, string? storeCode, IServiceProvider services)
    {
        try
        {
            switch (step.ActionType)
            {
                case "RestartService":
                    return await RestartServiceAsync(step, hostname);

                case "RunScript":
                    return await RunScriptAsync(step, hostname);

                case "SendAlert":
                    return await SendAlertAsync(step, hostname, storeCode, services);

                case "KillProcess":
                    return await KillProcessAsync(step, hostname);

                case "RestartDevice":
                    return await RestartDeviceAsync(hostname);

                case "ClearTempFiles":
                    return await ClearTempFilesAsync(hostname);

                case "Wait":
                    var waitSec = GetPayloadInt(step.ActionPayloadJson, "seconds", 60);
                    await Task.Delay(TimeSpan.FromSeconds(waitSec));
                    return (true, $"{waitSec}s beklendi.");

                default:
                    return (false, $"Bilinmeyen aksiyon: {step.ActionType}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Hata: {ex.Message}");
        }
    }

    private static async Task<(bool, string)> RestartServiceAsync(PlaybookStep step, string? hostname)
    {
        var serviceName = GetPayloadString(step.ActionPayloadJson, "serviceName");
        if (string.IsNullOrWhiteSpace(serviceName)) return (false, "serviceName belirtilmemiş.");
        if (string.IsNullOrWhiteSpace(hostname)) return (false, "Hedef cihaz hostname'i bilinmiyor.");

        var wmiUser = Environment.GetEnvironmentVariable("WMI_USER") ?? "MUDODMN\\mudoadmtd";
        var wmiPass = Environment.GetEnvironmentVariable("WMI_PASSWORD") ?? "";

        // wmic /node: DCOM kullanır, WSMan gerektirmez (saha PC'lerinde WSMan kapalı)
        var args = $@"/node:""{hostname}"" /user:""{wmiUser}"" /password:""{wmiPass}"" service where ""name='{serviceName}'"" call startservice";
        var result = await RunProcessAsync("wmic", args, timeoutSeconds: 30);

        if (result.success && result.output.Contains("ReturnValue = 0"))
            return (true, $"{serviceName} servisi başarıyla yeniden başlatıldı.");

        // Stop + Start dene
        var stopArgs = $@"/node:""{hostname}"" /user:""{wmiUser}"" /password:""{wmiPass}"" service where ""name='{serviceName}'"" call stopservice";
        await RunProcessAsync("wmic", stopArgs, timeoutSeconds: 15);
        await Task.Delay(2000);

        var startResult = await RunProcessAsync("wmic", args, timeoutSeconds: 30);
        if (startResult.success && startResult.output.Contains("ReturnValue = 0"))
            return (true, $"{serviceName} stop+start ile yeniden başlatıldı.");

        return (false, $"{serviceName} yeniden başlatılamadı. Çıktı: {result.output.Trim()}");
    }

    private static async Task<(bool, string)> RunScriptAsync(PlaybookStep step, string? hostname)
    {
        var script = GetPayloadString(step.ActionPayloadJson, "script");
        if (string.IsNullOrWhiteSpace(script)) return (false, "script belirtilmemiş.");
        if (string.IsNullOrWhiteSpace(hostname)) return (false, "Hedef cihaz hostname'i bilinmiyor.");

        var wmiUser = Environment.GetEnvironmentVariable("WMI_USER") ?? "MUDODMN\\mudoadmtd";
        var wmiPass = Environment.GetEnvironmentVariable("WMI_PASSWORD") ?? "";

        var escaped = script.Replace("\"", "\\\"");
        var args = $@"/node:""{hostname}"" /user:""{wmiUser}"" /password:""{wmiPass}"" process call create ""cmd.exe /c {escaped}""";
        var result = await RunProcessAsync("wmic", args, timeoutSeconds: 60);

        return result.success
            ? (true, "Script çalıştırıldı.")
            : (false, $"Script çalıştırılamadı: {result.output.Trim()}");
    }

    private static async Task<(bool, string)> KillProcessAsync(PlaybookStep step, string? hostname)
    {
        var processName = GetPayloadString(step.ActionPayloadJson, "processName");
        if (string.IsNullOrWhiteSpace(processName)) return (false, "processName belirtilmemiş.");
        if (string.IsNullOrWhiteSpace(hostname)) return (false, "Hedef cihaz hostname'i bilinmiyor.");

        var wmiUser = Environment.GetEnvironmentVariable("WMI_USER") ?? "MUDODMN\\mudoadmtd";
        var wmiPass = Environment.GetEnvironmentVariable("WMI_PASSWORD") ?? "";

        var args = $@"/node:""{hostname}"" /user:""{wmiUser}"" /password:""{wmiPass}"" process where ""name='{processName}'"" call terminate";
        var result = await RunProcessAsync("wmic", args, timeoutSeconds: 20);

        return result.success
            ? (true, $"{processName} sonlandırıldı.")
            : (false, $"{processName} sonlandırılamadı: {result.output.Trim()}");
    }

    private static async Task<(bool, string)> RestartDeviceAsync(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname)) return (false, "Hedef cihaz hostname'i bilinmiyor.");

        var wmiUser = Environment.GetEnvironmentVariable("WMI_USER") ?? "MUDODMN\\mudoadmtd";
        var wmiPass = Environment.GetEnvironmentVariable("WMI_PASSWORD") ?? "";

        var args = $@"/node:""{hostname}"" /user:""{wmiUser}"" /password:""{wmiPass}"" os call reboot";
        var result = await RunProcessAsync("wmic", args, timeoutSeconds: 30);

        return result.success
            ? (true, $"{hostname} yeniden başlatma komutu gönderildi.")
            : (false, $"Yeniden başlatma komutu gönderilemedi: {result.output.Trim()}");
    }

    private static async Task<(bool, string)> ClearTempFilesAsync(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname)) return (false, "Hedef cihaz hostname'i bilinmiyor.");

        var wmiUser = Environment.GetEnvironmentVariable("WMI_USER") ?? "MUDODMN\\mudoadmtd";
        var wmiPass = Environment.GetEnvironmentVariable("WMI_PASSWORD") ?? "";

        var script = @"del /q /f /s %TEMP%\* 2>nul & exit 0";
        var escaped = script.Replace("\"", "\\\"");
        var args = $@"/node:""{hostname}"" /user:""{wmiUser}"" /password:""{wmiPass}"" process call create ""cmd.exe /c {escaped}""";
        var result = await RunProcessAsync("wmic", args, timeoutSeconds: 60);

        return result.success
            ? (true, "Temp dosyaları temizleme komutu gönderildi.")
            : (false, $"Temp temizleme başarısız: {result.output.Trim()}");
    }

    private static async Task<(bool, string)> SendAlertAsync(PlaybookStep step, string? hostname, string? storeCode, IServiceProvider services)
    {
        var message = GetPayloadString(step.ActionPayloadJson, "message") ?? $"Playbook uyarısı: {hostname}";

        try
        {
            var emailService = services.GetRequiredService<IEmailService>();
            var db = services.GetRequiredService<OrchestraDbContext>();

            var recipients = await db.Users
                .Where(u => u.IsActive && u.Email != null && u.Email != "" && u.Role == "Admin")
                .Select(u => u.Email!)
                .ToListAsync();

            if (recipients.Count == 0) return (false, "E-posta alıcısı bulunamadı.");

            var subject = $"[Orchestra Playbook] {storeCode ?? hostname ?? "Bilinmiyor"} - Uyarı";
            var body = $@"<div style='font-family:Arial,sans-serif;max-width:600px'>
<div style='background:#1e1b4b;color:white;padding:16px 24px;border-radius:8px 8px 0 0'>
  <h2 style='margin:0'>Playbook Uyarısı</h2>
</div>
<div style='background:#f8fafc;padding:24px;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px'>
  <p><strong>Mağaza:</strong> {storeCode ?? "-"}</p>
  <p><strong>Cihaz:</strong> {hostname ?? "-"}</p>
  <p><strong>Mesaj:</strong> {message}</p>
  <hr style='border:none;border-top:1px solid #e2e8f0;margin:16px 0'/>
  <p style='color:#94a3b8;font-size:12px'>Bu otomatik bir bildirimdir — Orchestra Playbook Engine</p>
</div></div>";

            var result = await emailService.SendAlarmEmailAsync(recipients, subject, body);
            return (result.AllSucceeded || result.PartialSuccess, "Uyarı e-postası gönderildi.");
        }
        catch (Exception ex)
        {
            return (false, $"E-posta gönderilemedi: {ex.Message}");
        }
    }

    private static async Task<(bool success, string output)> RunProcessAsync(string file, string args, int timeoutSeconds = 30)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            proc.Start();
            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var errorTask = proc.StandardError.ReadToEndAsync();

            var exited = await Task.Run(() => proc.WaitForExit(timeoutSeconds * 1000));
            if (!exited) { proc.Kill(); return (false, "Zaman aşımı."); }

            var output = await outputTask + await errorTask;
            return (proc.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string? GetPayloadString(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    private static int GetPayloadInt(string? json, string key, int defaultVal)
    {
        if (string.IsNullOrWhiteSpace(json)) return defaultVal;
        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var v) && v.TryGetInt32(out var i) ? i : defaultVal;
        }
        catch { return defaultVal; }
    }
}
