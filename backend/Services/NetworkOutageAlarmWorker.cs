using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Services
{
    public class NetworkOutageAlarmWorker : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
        private const int ConfirmationThreshold = 2;
        private const int TimeoutMs = 700;
        private const int MaxConcurrency = 40;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NetworkOutageAlarmWorker> _log;
        private readonly IEmailService _emailService;
        private readonly ConcurrentDictionary<string, int> _scenarioStreaks = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastSentAt = new();

        public NetworkOutageAlarmWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<NetworkOutageAlarmWorker> log,
            IEmailService emailService)
        {
            _scopeFactory = scopeFactory;
            _log = log;
            _emailService = emailService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation(
                "NetworkOutageAlarmWorker starting (interval: {Interval}, confirmation: {Confirmation})",
                CheckInterval,
                ConfirmationThreshold);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndSendAlertsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Network outage alert cycle failed");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }

            _log.LogInformation("NetworkOutageAlarmWorker stopped");
        }

        private async Task CheckAndSendAlertsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MudoSoftDbContext>();
            var fastCheck = scope.ServiceProvider.GetRequiredService<FastSqlReachabilityService>();

            var config = await LoadAlarmConfigAsync(db, stoppingToken);
            if (config == null || !config.EmailAlertsEnabled)
                return;

            var recipients = await db.Users
                .AsNoTracking()
                .Where(u => u.IsActive
                    && u.Email != null
                    && u.Email != ""
                    && config.AlertRecipientRoles.Contains(u.Role))
                .Select(u => u.Email!)
                .Distinct()
                .ToListAsync(stoppingToken);

            if (recipients.Count == 0)
            {
                _log.LogWarning("Network alarm recipients not found (roles: {Roles})",
                    string.Join(", ", config.AlertRecipientRoles));
                return;
            }

            var devices = await LoadStoreDeviceStatusesAsync(db, fastCheck, stoppingToken);
            var candidates = BuildCandidates(devices);
            var activeKeys = candidates.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var key in _scenarioStreaks.Keys.ToList())
            {
                if (!activeKeys.Contains(key))
                    _scenarioStreaks.TryRemove(key, out _);
            }

            foreach (var candidate in candidates)
            {
                var streak = _scenarioStreaks.AddOrUpdate(candidate.Key, 1, (_, old) => old + 1);
                if (streak < ConfirmationThreshold)
                    continue;

                if (_lastSentAt.TryGetValue(candidate.Key, out var lastSent))
                {
                    var cooldownMinutes = config.CooldownMinutes > 0 ? config.CooldownMinutes : 30;
                    if ((DateTime.UtcNow - lastSent).TotalMinutes < cooldownMinutes)
                        continue;
                }

                var emailResult = await _emailService.SendAlarmEmailAsync(recipients, candidate.Subject, candidate.HtmlBody);
                if (emailResult.AllSucceeded || emailResult.PartialSuccess)
                {
                    _lastSentAt[candidate.Key] = DateTime.UtcNow;
                    _log.LogInformation("Network outage alarm sent: {ScenarioKey} ({StoreCode})",
                        candidate.Key, candidate.StoreCode);
                }
            }
        }

        private static async Task<AlarmConfig?> LoadAlarmConfigAsync(MudoSoftDbContext db, CancellationToken stoppingToken)
        {
            var setting = await db.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "alarm:config", stoppingToken);
            if (setting == null)
                return null;

            try
            {
                return JsonSerializer.Deserialize<AlarmConfig>(setting.Value);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<List<StoreDeviceWithStatusDto>> LoadStoreDeviceStatusesAsync(
            MudoSoftDbContext db,
            FastSqlReachabilityService fastCheck,
            CancellationToken stoppingToken)
        {
            var devices = await db.StoreDevices
                .AsNoTracking()
                .Select(d => new StoreDeviceWithStatusDto
                {
                    DeviceId = d.DeviceId,
                    StoreCode = d.StoreCode,
                    StoreName = d.StoreName,
                    DeviceType = d.DeviceType,
                    DeviceName = d.DeviceName,
                    CalculatedIpAddress = d.CalculatedIpAddress,
                    IsOnline = false,
                    LastSeen = d.LastSeen,
                    IsTemporarilyClosed = d.IsTemporarilyClosed,
                    TemporaryCloseReason = d.TemporaryCloseReason
                })
                .ToListAsync(stoppingToken);

            using var sem = new SemaphoreSlim(MaxConcurrency);

            var tasks = devices.Select(async d =>
            {
                await sem.WaitAsync(stoppingToken);
                try
                {
                    if (IsRouter(d))
                    {
                        // Router: sadece ping
                        var pingOk = await fastCheck.IsPingReachableAsync(d.CalculatedIpAddress, TimeoutMs);
                        d.PingReachable = pingOk;
                        d.IsOnline = pingOk;
                    }
                    else
                    {
                        // PC/Kasa/Gecici: Ping + SQL paralel kontrol
                        var (pingOk, sqlOk) = await fastCheck.CheckDeviceMultiAsync(d.CalculatedIpAddress, TimeoutMs);
                        d.PingReachable = pingOk;
                        d.SqlReachable = sqlOk;
                        d.IsOnline = sqlOk;
                    }
                }
                catch
                {
                    d.PingReachable = false;
                    d.SqlReachable = false;
                    d.IsOnline = false;
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
            return devices;
        }

        private List<NetworkAlertCandidate> BuildCandidates(List<StoreDeviceWithStatusDto> devices)
        {
            var candidates = new List<NetworkAlertCandidate>();

            var storeGroups = devices
                .Where(d => d.StoreCode > 1 && !d.IsTemporarilyClosed && !IsTemporary(d))
                .GroupBy(d => d.StoreCode)
                .OrderBy(g => g.Key);

            foreach (var group in storeGroups)
            {
                var storeDevices = group.ToList();
                if (storeDevices.Count == 0)
                    continue;

                var storeCode = group.Key;
                var storeName = storeDevices.First().StoreName;
                var router = storeDevices.FirstOrDefault(IsRouter);
                var pcDevices = storeDevices.Where(IsPc).OrderBy(d => d.DeviceName).ToList();
                var kasaDevices = storeDevices.Where(IsKasa).OrderBy(d => d.DeviceName).ToList();
                var nonRouterDevices = storeDevices.Where(d => !IsRouter(d)).ToList();

                var routerDown = router != null && IsRouterDown(router);
                var downNonRouter = nonRouterDevices.Where(IsEndpointDown).ToList();
                var downPcs = pcDevices.Where(IsEndpointDown).ToList();
                var downKasas = kasaDevices.Where(IsEndpointDown).ToList();

                if (router != null && routerDown && nonRouterDevices.Count > 0 && downNonRouter.Count == nonRouterDevices.Count)
                {
                    candidates.Add(BuildCandidate(
                        key: $"store:{storeCode}:full-outage",
                        storeCode: storeCode,
                        storeName: storeName,
                        title: "Tam Kesinti",
                        subject: $"[MudoSoft] Tam Kesinti: {storeCode} - {storeName}",
                        description: "Router ping vermiyor ve magaza cihazlarinin tamami erisilemez durumda.",
                        affectedDevices: new[] { router }.Concat(downNonRouter).ToList()));
                    continue;
                }

                if (router != null && !routerDown && nonRouterDevices.Count > 0 && downNonRouter.Count == nonRouterDevices.Count)
                {
                    candidates.Add(BuildCandidate(
                        key: $"store:{storeCode}:internal-network",
                        storeCode: storeCode,
                        storeName: storeName,
                        title: "Ic Ag Kesintisi",
                        subject: $"[MudoSoft] Ic Ag Kesintisi: {storeCode} - {storeName}",
                        description: "Router erisilebilir ancak PC ve kasalarin tamami erisilemez durumda.",
                        affectedDevices: downNonRouter));
                    continue;
                }

                if (router != null && routerDown)
                {
                    candidates.Add(BuildCandidate(
                        key: $"store:{storeCode}:router",
                        storeCode: storeCode,
                        storeName: storeName,
                        title: "Router Kesintisi",
                        subject: $"[MudoSoft] Router Kesintisi: {storeCode} - {storeName}",
                        description: "Router ping vermiyor. Magaza ag cikisi veya router cihazinda sorun olabilir.",
                        affectedDevices: new List<StoreDeviceWithStatusDto> { router }));
                }

                if (downPcs.Count > 0 && !(router != null && routerDown))
                {
                    candidates.Add(BuildCandidate(
                        key: $"store:{storeCode}:pc:{BuildAffectedKey(downPcs)}",
                        storeCode: storeCode,
                        storeName: storeName,
                        title: "PC Kesintisi",
                        subject: downPcs.Count == 1
                            ? $"[MudoSoft] PC Kesintisi: {storeCode} - {storeName}"
                            : $"[MudoSoft] {downPcs.Count} PC Kesintisi: {storeCode} - {storeName}",
                        description: "PC cihazlarinda dogrulanmis erisim kaybi tespit edildi.",
                        affectedDevices: downPcs));
                }

                if (downKasas.Count > 0 && !(router != null && routerDown))
                {
                    candidates.Add(BuildCandidate(
                        key: $"store:{storeCode}:kasa:{BuildAffectedKey(downKasas)}",
                        storeCode: storeCode,
                        storeName: storeName,
                        title: "Kasa Kesintisi",
                        subject: downKasas.Count == 1
                            ? $"[MudoSoft] Kasa Kesintisi: {storeCode} - {storeName}"
                            : $"[MudoSoft] {downKasas.Count} Kasa Kesintisi: {storeCode} - {storeName}",
                        description: "Kasa cihazlarinda dogrulanmis erisim kaybi tespit edildi.",
                        affectedDevices: downKasas));
                }
            }

            return candidates;
        }

        private static NetworkAlertCandidate BuildCandidate(
            string key,
            int storeCode,
            string storeName,
            string title,
            string subject,
            string description,
            List<StoreDeviceWithStatusDto> affectedDevices)
        {
            return new NetworkAlertCandidate(
                key,
                storeCode,
                subject,
                BuildHtmlBody(title, storeCode, storeName, description, affectedDevices));
        }

        private static string BuildHtmlBody(
            string title,
            int storeCode,
            string storeName,
            string description,
            List<StoreDeviceWithStatusDto> affectedDevices)
        {
            var rows = new StringBuilder();
            foreach (var device in affectedDevices.OrderBy(d => d.DeviceName))
            {
                var ping = device.PingReachable.HasValue ? (device.PingReachable.Value ? "OK" : "FAIL") : "-";
                var sql = device.SqlReachable.HasValue ? (device.SqlReachable.Value ? "OK" : "FAIL") : "-";

                rows.Append($@"
      <tr style='border-bottom: 1px solid #e2e8f0;'>
        <td style='padding: 10px 12px;'>{device.DeviceName}</td>
        <td style='padding: 10px 12px;'>{device.DeviceType}</td>
        <td style='padding: 10px 12px; font-family: monospace;'>{device.CalculatedIpAddress}</td>
        <td style='padding: 10px 12px;'>{ping}</td>
        <td style='padding: 10px 12px;'>{sql}</td>
      </tr>");
            }

            var confirmationMinutes = ConfirmationThreshold * (int)CheckInterval.TotalMinutes;

            return $@"
<div style='font-family: Arial, sans-serif; max-width: 760px; margin: 0 auto;'>
  <div style='background: #7c3aed; color: white; padding: 16px 24px; border-radius: 8px 8px 0 0;'>
    <h2 style='margin: 0; font-size: 18px;'>{title}</h2>
  </div>
  <div style='background: #f8fafc; padding: 24px; border: 1px solid #e2e8f0; border-top: none; border-radius: 0 0 8px 8px;'>
    <p style='margin-top: 0; color: #0f172a; font-weight: 600;'>{storeCode} - {storeName}</p>
    <p style='color: #334155;'>{description}</p>
    <p style='color: #64748b; font-size: 12px;'>Alarm, anlik kesintileri elemek icin {confirmationMinutes} dakika boyunca arka arkaya dogrulanan durumlarda gonderilir.</p>
    <table style='width: 100%; border-collapse: collapse; font-size: 14px; margin-top: 16px;'>
      <thead>
        <tr style='background: #f1f5f9; border-bottom: 2px solid #e2e8f0;'>
          <th style='text-align: left; padding: 10px 12px; color: #64748b; font-weight: 600;'>Cihaz</th>
          <th style='text-align: left; padding: 10px 12px; color: #64748b; font-weight: 600;'>Tip</th>
          <th style='text-align: left; padding: 10px 12px; color: #64748b; font-weight: 600;'>IP</th>
          <th style='text-align: left; padding: 10px 12px; color: #64748b; font-weight: 600;'>Ping</th>
          <th style='text-align: left; padding: 10px 12px; color: #64748b; font-weight: 600;'>SQL</th>
        </tr>
      </thead>
      <tbody>{rows}</tbody>
    </table>
    <div style='margin-top: 16px; color: #94a3b8; font-size: 12px;'>Bu otomatik bir bildirimdir - MudoSoft RMM</div>
  </div>
</div>";
        }

        private static string BuildAffectedKey(IEnumerable<StoreDeviceWithStatusDto> devices)
        {
            return string.Join(",", devices
                .OrderBy(d => d.DeviceId)
                .Select(d => d.DeviceId));
        }

        private static bool IsRouter(StoreDeviceWithStatusDto device)
        {
            return device.DeviceType.Equals("ROUTER", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPc(StoreDeviceWithStatusDto device)
        {
            return device.DeviceType.Equals("PC", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKasa(StoreDeviceWithStatusDto device)
        {
            return device.DeviceType.StartsWith("KASA", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTemporary(StoreDeviceWithStatusDto device)
        {
            return device.DeviceType.Equals("GECICI", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRouterDown(StoreDeviceWithStatusDto device)
        {
            return device.PingReachable == false || !device.IsOnline;
        }

        private static bool IsEndpointDown(StoreDeviceWithStatusDto device)
        {
            if (IsRouter(device))
                return IsRouterDown(device);

            return device.PingReachable == false && (device.SqlReachable == false || !device.IsOnline);
        }

        private sealed record NetworkAlertCandidate(string Key, int StoreCode, string Subject, string HtmlBody);

        private sealed class AlarmConfig
        {
            public bool EmailAlertsEnabled { get; set; }
            public string[] AlertRecipientRoles { get; set; } = new[] { "Admin" };
            public int CooldownMinutes { get; set; } = 30;
        }
    }
}
