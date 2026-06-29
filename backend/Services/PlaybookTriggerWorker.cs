using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services;

/// <summary>
/// Her 5 dakikada etkin playbook'ların trigger koşullarını kontrol eder.
/// Koşul sağlanırsa PlaybookEngine üzerinden çalıştırır.
/// </summary>
public class PlaybookTriggerWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    // Aynı cihaz için aynı playbook'u en fazla 30 dakikada 1 tetikle
    private static readonly TimeSpan TriggerCooldown = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlaybookEngine _engine;
    private readonly ILogger<PlaybookTriggerWorker> _logger;

    // (playbookId, deviceId) → son tetiklenme zamanı
    private readonly Dictionary<string, DateTime> _lastTriggered = new();

    public PlaybookTriggerWorker(IServiceScopeFactory scopeFactory, IPlaybookEngine engine, ILogger<PlaybookTriggerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _engine = engine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Açılışta 3 dakika bekle
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await EvaluateTriggersAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "PlaybookTriggerWorker döngü hatası"); }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task EvaluateTriggersAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

        var playbooks = await db.RemediationPlaybooks
            .AsNoTracking()
            .Include(p => p.Steps.OrderBy(s => s.StepOrder))
            .Where(p => p.IsEnabled)
            .ToListAsync();

        if (playbooks.Count == 0) return;

        foreach (var playbook in playbooks)
        {
            try
            {
                await EvaluatePlaybookAsync(playbook, db);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Playbook {Id} ({Name}) değerlendirilirken hata", playbook.Id, playbook.Name);
            }
        }
    }

    private async Task EvaluatePlaybookAsync(RemediationPlaybook playbook, OrchestraDbContext db)
    {
        switch (playbook.TriggerType)
        {
            case "ServiceDown":
                await TriggerOnServiceDown(playbook, db);
                break;
            case "DeviceOffline":
                await TriggerOnDeviceOffline(playbook, db);
                break;
            case "CpuHigh":
                await TriggerOnCpuHigh(playbook, db);
                break;
            case "DiskFull":
                await TriggerOnDiskFull(playbook, db);
                break;
            case "HealthScoreLow":
                await TriggerOnHealthScoreLow(playbook, db);
                break;
            case "MemoryHigh":
                await TriggerOnMemoryHigh(playbook, db);
                break;
            case "AgentSilent":
                await TriggerOnAgentSilent(playbook, db);
                break;
        }
    }

    private async Task TriggerOnServiceDown(RemediationPlaybook playbook, OrchestraDbContext db)
    {
        var serviceName = GetConditionString(playbook.TriggerConditionJson, "serviceName");

        var query = db.StoreServiceIncidents.AsNoTracking()
            .Where(i => i.ResolvedAt == null);

        if (!string.IsNullOrWhiteSpace(serviceName))
            query = query.Where(i => i.ServiceName == serviceName || i.DisplayName == serviceName);

        var incidents = await query.Take(20).ToListAsync();

        foreach (var incident in incidents)
        {
            var key = $"{playbook.Id}:{incident.DeviceId}:{incident.ServiceName}";
            if (!ShouldTrigger(key)) continue;

            _logger.LogInformation("Playbook {Name}: ServiceDown tetiklendi. Cihaz: {Device}, Servis: {Service}", playbook.Name, incident.DeviceName, incident.ServiceName);
            var scInc = incident.StoreCode.ToString();
            _ = Task.Run(() => _engine.ExecuteAsync(playbook, incident.DeviceId, incident.DeviceName, scInc,
                $"Servis çalışmıyor: {incident.DisplayName ?? incident.ServiceName}"));
            _lastTriggered[key] = DateTime.UtcNow;
        }
    }

    private async Task TriggerOnDeviceOffline(RemediationPlaybook playbook, OrchestraDbContext db)
    {
        var offlineDevices = await db.Devices.AsNoTracking()
            .Where(d => !d.Online && !d.IsTemporarilyClosed && !d.ExcludeFromOfflineList)
            .Take(50)
            .ToListAsync();

        foreach (var device in offlineDevices)
        {
            var key = $"{playbook.Id}:{device.Id}";
            if (!ShouldTrigger(key)) continue;

            _logger.LogInformation("Playbook {Name}: DeviceOffline tetiklendi. Cihaz: {Device}", playbook.Name, device.Hostname);
            var storeCodeStr = device.StoreCode.ToString();
            _ = Task.Run(() => _engine.ExecuteAsync(playbook, device.Id, device.Hostname, storeCodeStr,
                $"Cihaz offline: {device.Hostname}"));
            _lastTriggered[key] = DateTime.UtcNow;
        }
    }

    private async Task TriggerOnCpuHigh(RemediationPlaybook playbook, OrchestraDbContext db)
    {
        var threshold = GetConditionInt(playbook.TriggerConditionJson, "threshold", 90);

        var devices = await db.Devices.AsNoTracking()
            .Where(d => d.Online && d.CurrentCpuUsagePercent > threshold)
            .Take(20)
            .ToListAsync();

        foreach (var device in devices)
        {
            var key = $"{playbook.Id}:{device.Id}";
            if (!ShouldTrigger(key)) continue;

            _logger.LogInformation("Playbook {Name}: CpuHigh tetiklendi. Cihaz: {Device}, CPU: {Cpu}%", playbook.Name, device.Hostname, device.CurrentCpuUsagePercent);
            var sc1 = device.StoreCode.ToString();
            _ = Task.Run(() => _engine.ExecuteAsync(playbook, device.Id, device.Hostname, sc1,
                $"CPU yüksek: %{device.CurrentCpuUsagePercent:F0}"));
            _lastTriggered[key] = DateTime.UtcNow;
        }
    }

    private async Task TriggerOnDiskFull(RemediationPlaybook playbook, OrchestraDbContext db)
    {
        var threshold = GetConditionInt(playbook.TriggerConditionJson, "threshold", 90);

        var devices = await db.Devices.AsNoTracking()
            .Where(d => d.Online && d.CurrentDiskUsagePercent > threshold)
            .Take(20)
            .ToListAsync();

        foreach (var device in devices)
        {
            var key = $"{playbook.Id}:{device.Id}";
            if (!ShouldTrigger(key)) continue;

            _logger.LogInformation("Playbook {Name}: DiskFull tetiklendi. Cihaz: {Device}, Disk: {Disk}%", playbook.Name, device.Hostname, device.CurrentDiskUsagePercent);
            var sc2 = device.StoreCode.ToString();
            _ = Task.Run(() => _engine.ExecuteAsync(playbook, device.Id, device.Hostname, sc2,
                $"Disk dolmak üzere: %{device.CurrentDiskUsagePercent:F0}"));
            _lastTriggered[key] = DateTime.UtcNow;
        }
    }

    private async Task TriggerOnHealthScoreLow(RemediationPlaybook playbook, OrchestraDbContext db)
    {
        var threshold = GetConditionInt(playbook.TriggerConditionJson, "threshold", 40);

        var devices = await db.Devices.AsNoTracking()
            .Where(d => d.Online && d.HealthScore < threshold)
            .Take(20)
            .ToListAsync();

        foreach (var device in devices)
        {
            var key = $"{playbook.Id}:{device.Id}";
            if (!ShouldTrigger(key)) continue;

            _logger.LogInformation("Playbook {Name}: HealthScoreLow tetiklendi. Cihaz: {Device}, Score: {Score}", playbook.Name, device.Hostname, device.HealthScore);
            var sc3 = device.StoreCode.ToString();
            _ = Task.Run(() => _engine.ExecuteAsync(playbook, device.Id, device.Hostname, sc3,
                $"Sağlık skoru düşük: {device.HealthScore}"));
            _lastTriggered[key] = DateTime.UtcNow;
        }
    }

    private async Task TriggerOnMemoryHigh(RemediationPlaybook playbook, OrchestraDbContext db)
    {
        var threshold = GetConditionInt(playbook.TriggerConditionJson, "threshold", 85);

        var devices = await db.Devices.AsNoTracking()
            .Where(d => d.Online && d.CurrentRamUsagePercent > threshold)
            .Take(20)
            .ToListAsync();

        foreach (var device in devices)
        {
            var key = $"{playbook.Id}:{device.Id}";
            if (!ShouldTrigger(key)) continue;

            _logger.LogInformation("Playbook {Name}: MemoryHigh tetiklendi. Cihaz: {Device}, RAM: {Ram}%", playbook.Name, device.Hostname, device.CurrentRamUsagePercent);
            var sc = device.StoreCode.ToString();
            _ = Task.Run(() => _engine.ExecuteAsync(playbook, device.Id, device.Hostname, sc,
                $"RAM kullanımı yüksek: %{device.CurrentRamUsagePercent:F0}"));
            _lastTriggered[key] = DateTime.UtcNow;
        }
    }

    private async Task TriggerOnAgentSilent(RemediationPlaybook playbook, OrchestraDbContext db)
    {
        var minutesSilent = GetConditionInt(playbook.TriggerConditionJson, "minutesSilent", 60);
        var cutoff = DateTime.UtcNow.AddMinutes(-minutesSilent);

        var devices = await db.Devices.AsNoTracking()
            .Where(d => d.Online && d.LastSeen != null && d.LastSeen < cutoff)
            .Take(20)
            .ToListAsync();

        foreach (var device in devices)
        {
            var key = $"{playbook.Id}:{device.Id}";
            if (!ShouldTrigger(key)) continue;

            var silentMin = (int)(DateTime.UtcNow - device.LastSeen!.Value).TotalMinutes;
            _logger.LogInformation("Playbook {Name}: AgentSilent tetiklendi. Cihaz: {Device}, {Min} dk sessiz", playbook.Name, device.Hostname, silentMin);
            var sc = device.StoreCode.ToString();
            _ = Task.Run(() => _engine.ExecuteAsync(playbook, device.Id, device.Hostname, sc,
                $"Agent {silentMin} dakika yanıt vermiyor"));
            _lastTriggered[key] = DateTime.UtcNow;
        }
    }

    private bool ShouldTrigger(string key)
    {
        if (_lastTriggered.TryGetValue(key, out var last))
            return DateTime.UtcNow - last >= TriggerCooldown;
        return true;
    }

    private static string? GetConditionString(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.TryGetProperty(key, out var v) ? v.GetString() : null; }
        catch { return null; }
    }

    private static int GetConditionInt(string? json, string key, int def)
    {
        if (string.IsNullOrWhiteSpace(json)) return def;
        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var v) && v.TryGetInt32(out var i) ? i : def;
        }
        catch { return def; }
    }
}
