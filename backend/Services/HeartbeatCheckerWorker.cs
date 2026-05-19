using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services
{
    public class HeartbeatCheckerWorker : BackgroundService
    {
        private readonly ILogger<HeartbeatCheckerWorker> _log;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IEmailService _emailService;

        // Heartbeat timeout suresi (3 dakika — yuksek latency magazalar icin arttirildi)
        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(3);
        // Kontrol araligi (30 saniye)
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

        // Flap korumasi: cihaz basina ardisik miss sayaci
        // Bir cihaz offline olarak isaretlenmeden once RequiredConsecutiveMisses kadar
        // ardisik kontrol dongusunde stale olmasi gerekir
        private const int RequiredConsecutiveMisses = 3;
        private readonly ConcurrentDictionary<string, int> _consecutiveMisses = new();

        // Anti-spam: cihaz basina son alarm zamani
        private readonly ConcurrentDictionary<string, DateTime> _lastAlertSent = new();

        public HeartbeatCheckerWorker(IServiceScopeFactory scopeFactory, ILogger<HeartbeatCheckerWorker> log, IEmailService emailService)
        {
            _scopeFactory = scopeFactory;
            _log = log;
            _emailService = emailService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("HeartbeatCheckerWorker starting (timeout: {Timeout}, interval: {Interval})",
                HeartbeatTimeout, CheckInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndUpdateDeviceStatusAsync();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Heartbeat Checker cycle crashed");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }

            _log.LogInformation("HeartbeatCheckerWorker stopped");
        }

        private async Task CheckAndUpdateDeviceStatusAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            var cutoffTime = DateTime.UtcNow - HeartbeatTimeout;

            // Online olan ama son heartbeat'i timeout suresinden eski olan cihazlari bul
            var staleDevices = await dbContext.Devices
                .Where(d => d.Online && (d.LastSeen == null || d.LastSeen < cutoffTime))
                .ToListAsync();

            // Stale olmayan (saglıklı) online cihazlarin miss sayacini sifirla
            var healthyDevices = await dbContext.Devices
                .Where(d => d.Online && d.LastSeen != null && d.LastSeen >= cutoffTime)
                .Select(d => d.Id)
                .ToListAsync();

            foreach (var id in healthyDevices)
            {
                _consecutiveMisses.TryRemove(id, out _);
            }

            if (staleDevices.Count > 0)
            {
                var devicesToMarkOffline = new List<Device>();

                foreach (var device in staleDevices)
                {
                    var missCount = _consecutiveMisses.AddOrUpdate(device.Id, 1, (_, prev) => prev + 1);

                    if (missCount >= RequiredConsecutiveMisses)
                    {
                        device.Online = false;
                        devicesToMarkOffline.Add(device);
                        _consecutiveMisses.TryRemove(device.Id, out _);
                        _log.LogInformation(
                            "Device {DeviceId} ({Hostname}) marked as OFFLINE after {MissCount} consecutive misses (LastSeen: {LastSeen})",
                            device.Id, device.Hostname, missCount, device.LastSeen?.ToString("o") ?? "never");
                    }
                    else
                    {
                        _log.LogDebug(
                            "Device {DeviceId} ({Hostname}) stale but within grace period ({MissCount}/{Required})",
                            device.Id, device.Hostname, missCount, RequiredConsecutiveMisses);
                    }
                }

                if (devicesToMarkOffline.Count > 0)
                {
                    await dbContext.SaveChangesAsync();
                    _log.LogInformation("Marked {Count} device(s) as offline due to heartbeat timeout", devicesToMarkOffline.Count);

                    var offlineIds = devicesToMarkOffline.Select(d => d.Id).ToList();
                    // Alarm e-postasi gonder (arka planda, heartbeat dongusunu bloklama)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SendOfflineAlarmAsync(offlineIds);
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Offline alarm e-postasi gonderiminde hata");
                        }
                    });
                }
            }

            // Online'a donen cihazlarin cooldown'ini temizle
            CleanupCooldowns(dbContext);
        }

        private async Task SendOfflineAlarmAsync(List<string> offlineDeviceIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            var offlineDevices = await dbContext.Devices
                .Where(d => offlineDeviceIds.Contains(d.Id)
                    && !d.Online
                    && !d.ExcludeFromOfflineList
                    && !d.IsTemporarilyClosed)
                .ToListAsync();

            if (offlineDevices.Count == 0) return;

            // Alarm ayarlarini oku
            var alarmSetting = await dbContext.AppSettings.FindAsync("alarm:config");
            if (alarmSetting == null) return;

            AlarmConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<AlarmConfig>(alarmSetting.Value);
            }
            catch { return; }

            if (config == null || !config.EmailAlertsEnabled) return;

            var cooldownMinutes = config.CooldownMinutes > 0 ? config.CooldownMinutes : 30;

            // Cooldown kontrolu: sadece yeni offline olan cihazlari filtrele
            var devicesToAlert = offlineDevices.Where(d =>
            {
                var deviceKey = d.Id.ToString();
                if (_lastAlertSent.TryGetValue(deviceKey, out var lastSent))
                {
                    if ((DateTime.UtcNow - lastSent).TotalMinutes < cooldownMinutes)
                        return false;
                }
                return true;
            }).ToList();

            if (devicesToAlert.Count == 0) return;

            // Alici e-posta adreslerini bul
            var roles = config.AlertRecipientRoles ?? new[] { "Admin" };
            var recipients = await dbContext.Users
                .Where(u => u.IsActive && u.Email != null && u.Email != "" && roles.Contains(u.Role))
                .Select(u => u.Email!)
                .ToListAsync();

            if (recipients.Count == 0)
            {
                _log.LogWarning("Alarm alicisi bulunamadi (roller: {Roles})", string.Join(", ", roles));
                return;
            }

            // StoreDevice bilgilerini cek (magaza adi icin)
            var storeCodes = devicesToAlert.Select(d => d.StoreCode).Distinct().ToList();
            var storeDevices = await dbContext.StoreDevices
                .Where(sd => storeCodes.Contains(sd.StoreCode))
                .GroupBy(sd => sd.StoreCode)
                .ToDictionaryAsync(g => g.Key, g => g.First());

            // E-posta icerigini olustur
            string subject;
            string body;

            if (devicesToAlert.Count == 1)
            {
                var d = devicesToAlert[0];
                var sd = storeDevices.GetValueOrDefault(d.StoreCode);
                var storeName = sd?.StoreName ?? "";
                var displayName = !string.IsNullOrEmpty(storeName) ? $"{storeName} - {d.Hostname}" : d.Hostname;
                subject = $"[Orchestra] Cihaz Cevrimdisi: {displayName}";
                body = BuildSingleDeviceHtml(d, storeDevices);
            }
            else
            {
                subject = $"[Orchestra] {devicesToAlert.Count} Cihaz Cevrimdisi Oldu";
                body = BuildMultiDeviceHtml(devicesToAlert, storeDevices);
            }

            var emailResult = await _emailService.SendAlarmEmailAsync(recipients, subject, body);

            if (emailResult.AllSucceeded || emailResult.PartialSuccess)
            {
                // Cooldown kaydet
                foreach (var d in devicesToAlert)
                {
                    _lastAlertSent[d.Id.ToString()] = DateTime.UtcNow;
                }
                _log.LogInformation("Offline alarm e-postasi gonderildi: {Count} cihaz, {Recipients} alici",
                    devicesToAlert.Count, recipients.Count);
            }
        }

        private void CleanupCooldowns(OrchestraDbContext dbContext)
        {
            // Online olan cihazlarin cooldown'ini temizle
            foreach (var key in _lastAlertSent.Keys.ToList())
            {
                var device = dbContext.Devices.Find(key);
                if (device != null && device.Online)
                {
                    _lastAlertSent.TryRemove(key, out _);
                }
            }
        }

        private static string BuildSingleDeviceHtml(Device device, Dictionary<int, StoreDevice> storeDevices)
        {
            var sd = storeDevices.GetValueOrDefault(device.StoreCode);
            var storeName = sd?.StoreName ?? "-";
            var storeCode = device.StoreCode > 0 ? device.StoreCode.ToString() : "-";
            var lastSeen = device.LastSeen?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "Bilinmiyor";

            return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
  <div style='background: #1e1b4b; color: white; padding: 16px 24px; border-radius: 8px 8px 0 0;'>
    <h2 style='margin: 0; font-size: 18px;'>Cihaz Cevrimdisi Bildirimi</h2>
  </div>
  <div style='background: #f8fafc; padding: 24px; border: 1px solid #e2e8f0; border-top: none; border-radius: 0 0 8px 8px;'>
    <table style='width: 100%; border-collapse: collapse;'>
      <tr><td style='padding: 8px 0; color: #64748b; width: 140px;'>Magaza:</td><td style='padding: 8px 0; font-weight: 600;'>{storeCode} - {storeName}</td></tr>
      <tr><td style='padding: 8px 0; color: #64748b;'>Cihaz:</td><td style='padding: 8px 0; font-weight: 600;'>{device.Hostname ?? device.Id.ToString()}</td></tr>
      <tr><td style='padding: 8px 0; color: #64748b;'>IP Adresi:</td><td style='padding: 8px 0; font-family: monospace;'>{device.IpAddress ?? "-"}</td></tr>
      <tr><td style='padding: 8px 0; color: #64748b;'>Son Gorulme:</td><td style='padding: 8px 0;'>{lastSeen}</td></tr>
    </table>
    <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 16px 0;' />
    <p style='color: #94a3b8; font-size: 12px; margin: 0;'>Bu otomatik bir bildirimdir — Orchestra</p>
  </div>
</div>";
        }

        private static string BuildMultiDeviceHtml(List<Device> devices, Dictionary<int, StoreDevice> storeDevices)
        {
            var rows = new StringBuilder();
            foreach (var d in devices)
            {
                var sd = storeDevices.GetValueOrDefault(d.StoreCode);
                var storeName = sd != null ? $"{sd.StoreCode} - {sd.StoreName}" : "-";
                var lastSeen = d.LastSeen?.ToLocalTime().ToString("HH:mm") ?? "-";
                rows.Append($@"
      <tr style='border-bottom: 1px solid #e2e8f0;'>
        <td style='padding: 10px 12px;'>{storeName}</td>
        <td style='padding: 10px 12px; font-weight: 600;'>{d.Hostname ?? d.Id.ToString()}</td>
        <td style='padding: 10px 12px; font-family: monospace; font-size: 13px;'>{d.IpAddress ?? "-"}</td>
        <td style='padding: 10px 12px;'>{lastSeen}</td>
      </tr>");
            }

            return $@"
<div style='font-family: Arial, sans-serif; max-width: 700px; margin: 0 auto;'>
  <div style='background: #1e1b4b; color: white; padding: 16px 24px; border-radius: 8px 8px 0 0;'>
    <h2 style='margin: 0; font-size: 18px;'>{devices.Count} Cihaz Cevrimdisi Oldu</h2>
  </div>
  <div style='background: #f8fafc; padding: 0; border: 1px solid #e2e8f0; border-top: none; border-radius: 0 0 8px 8px;'>
    <table style='width: 100%; border-collapse: collapse; font-size: 14px;'>
      <thead>
        <tr style='background: #f1f5f9; border-bottom: 2px solid #e2e8f0;'>
          <th style='text-align: left; padding: 10px 12px; color: #64748b; font-weight: 600;'>Magaza</th>
          <th style='text-align: left; padding: 10px 12px; color: #64748b; font-weight: 600;'>Cihaz</th>
          <th style='text-align: left; padding: 10px 12px; color: #64748b; font-weight: 600;'>IP Adresi</th>
          <th style='text-align: left; padding: 10px 12px; color: #64748b; font-weight: 600;'>Son Gorulme</th>
        </tr>
      </thead>
      <tbody>{rows}</tbody>
    </table>
    <div style='padding: 16px 12px;'>
      <p style='color: #94a3b8; font-size: 12px; margin: 0;'>Bu otomatik bir bildirimdir — Orchestra</p>
    </div>
  </div>
</div>";
        }

        private class AlarmConfig
        {
            public bool EmailAlertsEnabled { get; set; }
            public string[]? AlertRecipientRoles { get; set; }
            public int CooldownMinutes { get; set; } = 30;
        }
    }
}
