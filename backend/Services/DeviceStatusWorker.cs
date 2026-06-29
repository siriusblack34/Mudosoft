using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services
{
    /// <summary>
    /// Arka planda periyodik olarak tüm cihazların online/offline durumunu tarar.
    /// HTTP endpoint (SqlQueryController) sadece bu cache'den döner — kullanıcı hiç beklemez.
    /// </summary>
    public class DeviceStatusWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly FastSqlReachabilityService _fastCheck;
        private readonly ILogger<DeviceStatusWorker> _log;

        private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(45);
        private const int MaxConcurrency = 20;               // 40→20: yuksek latency magazalarda TCP stack bogulmasini onle
        private const int RouterConcurrency = 3;             // Router'lar ayrı, düşük eşzamanlılıkla — ICMP stack boğulmasın
        private const int TimeoutMs = 1500;                  // 500→1500: yuksek latency magazalar icin (ortalama ping 300-600ms)
        private const int RouterTimeoutMs = 3000;  // 4.5G yedek hatta ping 500-1500ms olabilir, timeout'u genis tut
        private const int RouterFailThreshold = 3;           // Ardışık bu kadar başarısız taramadan sonra offline
        private const int DeviceFailThreshold = 3;           // PC/Kasa icin de ardisik basarisizlik esigi
        private const int PrinterFailThreshold = 1;          // Yazıcılar: ilk başarısızlıkta anında offline

        // Paylaşılan cache — SqlQueryController buradan okur
        private static List<StoreDeviceWithStatusDto>? _cache;
        private static DateTime _cacheTime = DateTime.MinValue;
        private static readonly object _cacheLock = new();

        // Durum geçişi takibi
        private static readonly Dictionary<string, bool> _previousStatus = new();
        // Router ardışık başarısızlık sayacı (deviceId → consecutive fail count)
        private static readonly Dictionary<string, int> _routerFailCount = new();
        // PC/Kasa ardisik basarisizlik sayaci (deviceId → consecutive fail count)
        private static readonly Dictionary<string, int> _deviceFailCount = new();
        // Yazıcı ardışık başarısızlık sayacı
        private static readonly Dictionary<string, int> _printerFailCount = new();

        public DeviceStatusWorker(
            IServiceScopeFactory scopeFactory,
            FastSqlReachabilityService fastCheck,
            ILogger<DeviceStatusWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _fastCheck = fastCheck;
            _log = logger;
        }

        /// <summary>Cache'den cihaz listesini döner. Cache boşsa null.</summary>
        public static List<StoreDeviceWithStatusDto>? GetCachedDevices() => _cache;
        public static DateTime GetCacheTime() => _cacheTime;

        /// <summary>Cache'i invalidate et — sonraki taramada yeniden doldurulur.</summary>
        public static void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cache = null;
                _cacheTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Cache'deki tek bir cihazı güncelle (cache'i null yapmadan).
        /// TemporaryClose gibi işlemlerde tüm cache'i invalidate etmek yerine bunu kullan.
        /// </summary>
        public static void UpdateCachedDevice(string deviceId, Action<StoreDeviceWithStatusDto> updater)
        {
            lock (_cacheLock)
            {
                if (_cache == null) return;
                var device = _cache.FirstOrDefault(d => d.DeviceId == deviceId);
                if (device != null)
                {
                    updater(device);
                }
            }
        }

        /// <summary>
        /// Cache'den bir cihazı kaldır (silme işlemleri için).
        /// </summary>
        public static void RemoveFromCache(string deviceId)
        {
            lock (_cacheLock)
            {
                _cache?.RemoveAll(d => d.DeviceId == deviceId);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("DeviceStatusWorker starting (interval: {Interval}, concurrency: {Concurrency})",
                ScanInterval, MaxConcurrency);

            // Restart öncesi offline olan cihazları hatırla — ilk taramada LastSeen sıfırlanmasın
            try { await SeedInitialStateAsync(stoppingToken); }
            catch (Exception ex) { _log.LogWarning(ex, "DeviceStatusWorker initial state seed başarısız (non-critical)"); }

            // İlk taramayı hemen yap — exception burada da yakalanmalı (host crash'i önler)
            try { await RunScanAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "DeviceStatusWorker ilk tarama başarısız, servis çalışmaya devam edecek"); }

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(ScanInterval, stoppingToken);

                try
                {
                    await RunScanAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "DeviceStatusWorker scan cycle failed");
                }
            }

            _log.LogInformation("DeviceStatusWorker stopped");
        }

        /// <summary>
        /// Backend restart sonrası cihazların son bilinen durumunu DB'den yükler.
        /// Offline olan cihazların fail sayacını eşiğe doldurur — ilk taramada da offline kalırlar
        /// ve LastSeen yanlışlıkla "şimdi" olarak güncellenmez.
        /// </summary>
        private async Task SeedInitialStateAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            // Her cihaz için en yüksek Id'li (= en son) kaydı bul
            var maxIds = await db.DeviceStatusChanges
                .AsNoTracking()
                .GroupBy(c => c.DeviceId)
                .Select(g => new { DeviceId = g.Key, MaxId = g.Max(c => c.Id) })
                .ToListAsync(ct);

            if (maxIds.Count == 0) return;

            var idSet = maxIds.Select(x => x.MaxId).ToHashSet();
            var lastChanges = await db.DeviceStatusChanges
                .AsNoTracking()
                .Where(c => idSet.Contains(c.Id))
                .ToListAsync(ct);

            int seededOffline = 0;
            foreach (var change in lastChanges)
            {
                _previousStatus[change.DeviceId] = change.IsOnline;

                if (!change.IsOnline)
                {
                    seededOffline++;
                    if (change.DeviceType.Equals("ROUTER", StringComparison.OrdinalIgnoreCase))
                        lock (_routerFailCount) { _routerFailCount[change.DeviceId] = RouterFailThreshold; }
                    else
                        lock (_deviceFailCount) { _deviceFailCount[change.DeviceId] = DeviceFailThreshold; }
                }
            }

            _log.LogInformation("DeviceStatusWorker: {Total} cihaz durumu yüklendi, {Offline} offline olarak işaretlendi",
                lastChanges.Count, seededOffline);
        }

        private async Task RunScanAsync(CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            // 1) DB'den tüm cihazları al
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
                    TemporaryCloseReason = d.TemporaryCloseReason,
                    WindowsVersion = d.WindowsVersion
                })
                .ToListAsync(ct);

            // 2a) Router'ları ÖNCE ve AYRI düşük eşzamanlılıkla tara
            //     (ICMP-only cihazlar — yüksek eşzamanlılıkta OS ping stack boğuluyor)
            var routers  = devices.Where(d => d.DeviceType.Equals("ROUTER", StringComparison.OrdinalIgnoreCase)).ToList();
            var printers = devices.Where(d => d.DeviceType.StartsWith("Yazici-", StringComparison.OrdinalIgnoreCase)).ToList();
            var others   = devices.Where(d => !d.DeviceType.Equals("ROUTER", StringComparison.OrdinalIgnoreCase)
                                           && !d.DeviceType.StartsWith("Yazici-", StringComparison.OrdinalIgnoreCase)).ToList();

            var routerSamples = new System.Collections.Concurrent.ConcurrentBag<RouterLatencySample>();
            using var routerSem = new SemaphoreSlim(RouterConcurrency);
            var routerTasks = routers.Select(async d =>
            {
                await routerSem.WaitAsync(ct);
                try
                {
                    var (pingOk, rtt) = await _fastCheck.PingRouterWithLatencyAsync(d.CalculatedIpAddress, RouterTimeoutMs);
                    d.PingReachable = pingOk;
                    d.LatencyMs = rtt;

                    // Her scan icin bir sample kaydet (hat tipi tespiti icin)
                    if (!d.IsTemporarilyClosed && !string.IsNullOrWhiteSpace(d.CalculatedIpAddress))
                    {
                        routerSamples.Add(new RouterLatencySample
                        {
                            DeviceId = d.DeviceId,
                            StoreCode = d.StoreCode,
                            Ip = d.CalculatedIpAddress,
                            RttMs = rtt,
                            Success = pingOk,
                            SampledAt = DateTime.UtcNow
                        });
                    }

                    // Ardışık başarısızlık sayacı — tek scan hatasıyla offline yapma
                    lock (_routerFailCount)
                    {
                        if (pingOk)
                        {
                            _routerFailCount.Remove(d.DeviceId);
                            d.IsOnline = true;
                        }
                        else
                        {
                            _routerFailCount.TryGetValue(d.DeviceId, out var fails);
                            fails++;
                            _routerFailCount[d.DeviceId] = fails;
                            d.IsOnline = fails < RouterFailThreshold;
                        }
                    }
                }
                finally
                {
                    routerSem.Release();
                }
            });
            await Task.WhenAll(routerTasks);

            // 2b) Yazıcılar — ICMP-only, threshold=1 (ilk başarısızlıkta anında offline)
            //     SQL portu denenmez: OKC yazıcılar port 1433'e cevap vermez, false-positive üretir
            using var printerSem = new SemaphoreSlim(MaxConcurrency);
            var printerTasks = printers.Select(async d =>
            {
                await printerSem.WaitAsync(ct);
                try
                {
                    var pingOk = !d.IsTemporarilyClosed && await _fastCheck.IsPingReachableAsync(d.CalculatedIpAddress, TimeoutMs);
                    d.PingReachable = pingOk;

                    lock (_printerFailCount)
                    {
                        if (pingOk)
                        {
                            _printerFailCount.Remove(d.DeviceId);
                            d.IsOnline = true;
                        }
                        else
                        {
                            _printerFailCount.TryGetValue(d.DeviceId, out var fails);
                            fails++;
                            _printerFailCount[d.DeviceId] = fails;
                            d.IsOnline = fails < PrinterFailThreshold; // 1: ilk başarısızlıkta offline
                        }
                    }
                }
                finally
                {
                    printerSem.Release();
                }
            });
            await Task.WhenAll(printerTasks);

            // 2c) Diğer cihazlar (PC/Kasa) — ping + TCP 1433 paralel
            //     IsOnline = ping VEYA sql basariliysa online say (ping yeterli, SQL durumu ayrica gosterilir)
            //     Flap korumasi: ardisik DeviceFailThreshold kadar basarisiz scan olmadan offline yapma
            using var sem = new SemaphoreSlim(MaxConcurrency);
            var otherTasks = others.Select(async d =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var (pingOk, sqlOk) = await _fastCheck.CheckDeviceMultiAsync(d.CalculatedIpAddress, TimeoutMs);
                    d.PingReachable = pingOk;
                    d.SqlReachable = sqlOk;

                    var reachable = pingOk || sqlOk;

                    lock (_deviceFailCount)
                    {
                        if (reachable)
                        {
                            _deviceFailCount.Remove(d.DeviceId);
                            d.IsOnline = true;
                        }
                        else
                        {
                            _deviceFailCount.TryGetValue(d.DeviceId, out var fails);
                            fails++;
                            _deviceFailCount[d.DeviceId] = fails;
                            d.IsOnline = fails < DeviceFailThreshold;
                        }
                    }
                }
                finally
                {
                    sem.Release();
                }
            });
            await Task.WhenAll(otherTasks);

            // 3) Online cihazların LastSeen güncelle
            var onlineIds = devices.Where(d => d.IsOnline).Select(d => d.DeviceId).ToHashSet();
            if (onlineIds.Count > 0)
            {
                // Tracking ile yeni scope (AsNoTracking yukarıda kullanıldı)
                using var writeScope = _scopeFactory.CreateScope();
                var writeDb = writeScope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

                var now = DateTime.UtcNow;
                var toUpdate = await writeDb.StoreDevices
                    .Where(d => onlineIds.Contains(d.DeviceId))
                    .ToListAsync(ct);
                foreach (var d in toUpdate)
                    d.LastSeen = now;

                // Durum geçişlerini takip et
                var changes = new List<DeviceStatusChange>();
                foreach (var d in devices)
                {
                    if (d.IsTemporarilyClosed) continue;
                    if (_previousStatus.TryGetValue(d.DeviceId, out var wasOnline) && wasOnline != d.IsOnline)
                    {
                        changes.Add(new DeviceStatusChange
                        {
                            DeviceId = d.DeviceId,
                            StoreCode = d.StoreCode,
                            DeviceType = d.DeviceType,
                            IsOnline = d.IsOnline,
                            ChangedAt = now
                        });
                    }
                    _previousStatus[d.DeviceId] = d.IsOnline;
                }

                if (changes.Count > 0)
                {
                    writeDb.DeviceStatusChanges.AddRange(changes);
                    _log.LogInformation("Tracked {Count} device status changes", changes.Count);
                }

                // Offline store logging
                try
                {
                    await UpdateOfflineLogsAsync(writeDb, devices, now);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Offline log update failed (non-critical)");
                }

                await writeDb.SaveChangesAsync(ct);

                foreach (var d in devices.Where(d => d.IsOnline))
                    d.LastSeen = now;
            }

            // 3.5) Router latency samples — karasal/mobil hat tespiti icin
            if (!routerSamples.IsEmpty)
            {
                try
                {
                    using var sampleScope = _scopeFactory.CreateScope();
                    var sampleDb = sampleScope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
                    sampleDb.RouterLatencySamples.AddRange(routerSamples);
                    await sampleDb.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Router latency sample save failed (non-critical)");
                }
            }

            // 4) Cache'i güncelle
            lock (_cacheLock)
            {
                _cache = devices;
                _cacheTime = DateTime.UtcNow;
            }

            sw.Stop();
            int routerPendingFails;
            lock (_routerFailCount)
            {
                routerPendingFails = _routerFailCount.Count(kv => kv.Value > 0 && kv.Value < RouterFailThreshold);
            }
            _log.LogInformation(
                "DeviceStatusWorker scan completed: {Count} devices in {Elapsed}ms ({Online} online, {RouterPending} router(s) pending confirmation)",
                devices.Count, sw.ElapsedMilliseconds, onlineIds.Count, routerPendingFails);
        }

        private static async Task UpdateOfflineLogsAsync(OrchestraDbContext db, List<StoreDeviceWithStatusDto> devices, DateTime now)
        {
            var storeStatus = devices
                .Where(d => !string.Equals(d.DeviceType, "PC", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(d.DeviceType, "ROUTER", StringComparison.OrdinalIgnoreCase)
                         && !d.IsTemporarilyClosed)
                .GroupBy(d => d.StoreCode)
                .Select(g => new
                {
                    StoreCode = g.Key,
                    StoreName = g.First().StoreName,
                    AllOffline = g.All(k => !k.IsOnline),
                    OfflineCount = g.Count(k => !k.IsOnline)
                })
                .ToList();

            var openLogs = await db.StoreOfflineLogs
                .Where(l => l.OnlineAt == null)
                .ToListAsync();

            var openLogMap = openLogs.ToDictionary(l => l.StoreCode);

            foreach (var store in storeStatus)
            {
                if (store.AllOffline)
                {
                    if (!openLogMap.ContainsKey(store.StoreCode))
                    {
                        db.StoreOfflineLogs.Add(new StoreOfflineLog
                        {
                            StoreCode = store.StoreCode,
                            StoreName = store.StoreName,
                            OfflineKasaCount = store.OfflineCount,
                            OfflineAt = now
                        });
                    }
                }
                else
                {
                    if (openLogMap.TryGetValue(store.StoreCode, out var log))
                    {
                        log.OnlineAt = now;
                        log.DurationMinutes = (int)(now - log.OfflineAt).TotalMinutes;
                    }
                }
            }
        }
    }
}
