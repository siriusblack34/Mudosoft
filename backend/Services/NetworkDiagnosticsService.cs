using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Services
{
    // ─── Teshis Tipleri ───
    public enum DiagnosticType
    {
        FullOutage,         // Router + tum cihazlar offline → ISP/elektrik
        InternalNetwork,    // Router online, cihazlar offline → switch/kablo
        RouterFlapping,     // Router kisa aralıklarla on/off → modem/hat
        DeviceFlapping,     // Tek cihaz kisa aralıklarla on/off → cihaz sorunu
        PartialOutage,      // Bazi cihazlar offline, bazilari online → kısmi ariza
        StoreFlapping,      // Tum magaza aralikli kesinti → kararsiz hat
    }

    public enum DiagnosticSeverity
    {
        Critical,   // Acil mudahale
        Warning,    // Izleme
        Info        // Bilgi
    }

    public class StoreDiagnostic
    {
        public int StoreCode { get; set; }
        public string StoreName { get; set; } = "";
        public DiagnosticType Type { get; set; }
        public DiagnosticSeverity Severity { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime DetectedAt { get; set; }

        // Detay bilgileri
        public bool RouterOnline { get; set; }
        public int TotalDevices { get; set; }
        public int OnlineDevices { get; set; }
        public int OfflineDevices { get; set; }
        public int FlappingCount { get; set; }  // son X dk icinde kac gecis
        public List<string> AffectedDevices { get; set; } = new();
    }

    public class NetworkDiagnosticsService
    {
        private readonly MudoSoftDbContext _db;

        public NetworkDiagnosticsService(MudoSoftDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Tum magazalari analiz edip aktif teshisleri dondurur.
        /// currentDevices: en son durum kontrolunun sonuclari
        /// </summary>
        public async Task<List<StoreDiagnostic>> AnalyzeAllStoresAsync(
            List<StoreDeviceWithStatusDto> currentDevices,
            int flappingWindowMinutes = 30,
            int flappingThreshold = 4)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddMinutes(-flappingWindowMinutes);
            var diagnostics = new List<StoreDiagnostic>();

            // Son X dakikadaki tum durum gecislerini al
            var recentChanges = await _db.DeviceStatusChanges
                .AsNoTracking()
                .Where(c => c.ChangedAt >= windowStart)
                .ToListAsync();

            var changesByStore = recentChanges
                .GroupBy(c => c.StoreCode)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Gecici kapali olmayan cihazlari magazaya gore grupla
            var storeGroups = currentDevices
                .Where(d => !d.IsTemporarilyClosed && d.StoreCode > 1)
                .GroupBy(d => d.StoreCode)
                .ToList();

            foreach (var store in storeGroups)
            {
                var storeCode = store.Key;
                var storeName = store.First().StoreName;
                var devices = store.ToList();

                var router = devices.FirstOrDefault(d =>
                    d.DeviceType.Equals("ROUTER", StringComparison.OrdinalIgnoreCase));
                var nonRouterDevices = devices
                    .Where(d => !d.DeviceType.Equals("ROUTER", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (nonRouterDevices.Count == 0) continue;

                var onlineNonRouter = nonRouterDevices.Count(d => d.IsOnline);
                var offlineNonRouter = nonRouterDevices.Count(d => !d.IsOnline);
                var routerOnline = router?.IsOnline ?? true; // router yoksa online kabul et

                changesByStore.TryGetValue(storeCode, out var storeChanges);
                storeChanges ??= new List<DeviceStatusChange>();

                // ─── 1. TAM KESİNTİ: Router offline + tum cihazlar offline ───
                if (router != null && !routerOnline && offlineNonRouter == nonRouterDevices.Count)
                {
                    diagnostics.Add(new StoreDiagnostic
                    {
                        StoreCode = storeCode,
                        StoreName = storeName,
                        Type = DiagnosticType.FullOutage,
                        Severity = DiagnosticSeverity.Critical,
                        Title = "Tam Kesinti",
                        Message = $"Router ve tum cihazlar offline. ISP veya elektrik kesintisi olabilir.",
                        DetectedAt = now,
                        RouterOnline = false,
                        TotalDevices = nonRouterDevices.Count,
                        OnlineDevices = 0,
                        OfflineDevices = offlineNonRouter,
                        AffectedDevices = nonRouterDevices.Select(d => d.DeviceName).ToList()
                    });
                    continue; // Bu magaza icin baska teshis bakma
                }

                // ─── 2. İÇ AĞ SORUNU: Router online ama cihazlar offline ───
                if (routerOnline && offlineNonRouter == nonRouterDevices.Count)
                {
                    diagnostics.Add(new StoreDiagnostic
                    {
                        StoreCode = storeCode,
                        StoreName = storeName,
                        Type = DiagnosticType.InternalNetwork,
                        Severity = DiagnosticSeverity.Critical,
                        Title = "İç Ağ Sorunu",
                        Message = $"Router aktif ama {offlineNonRouter} cihaz offline. Switch veya kablo arızası olabilir.",
                        DetectedAt = now,
                        RouterOnline = true,
                        TotalDevices = nonRouterDevices.Count,
                        OnlineDevices = 0,
                        OfflineDevices = offlineNonRouter,
                        AffectedDevices = nonRouterDevices.Where(d => !d.IsOnline).Select(d => d.DeviceName).ToList()
                    });
                    continue;
                }

                // ─── 3. ROUTER FLAPPING: Router kisa aralıklarla on/off ───
                if (router != null)
                {
                    var routerChanges = storeChanges
                        .Where(c => c.DeviceId == router.DeviceId)
                        .OrderBy(c => c.ChangedAt)
                        .ToList();

                    if (routerChanges.Count >= flappingThreshold)
                    {
                        diagnostics.Add(new StoreDiagnostic
                        {
                            StoreCode = storeCode,
                            StoreName = storeName,
                            Type = DiagnosticType.RouterFlapping,
                            Severity = DiagnosticSeverity.Warning,
                            Title = "Router Kararsız",
                            Message = $"Router son {flappingWindowMinutes} dk icinde {routerChanges.Count} kez durum degistirdi. Modem/hat sorunu olabilir.",
                            DetectedAt = now,
                            RouterOnline = routerOnline,
                            TotalDevices = nonRouterDevices.Count,
                            OnlineDevices = onlineNonRouter,
                            OfflineDevices = offlineNonRouter,
                            FlappingCount = routerChanges.Count
                        });
                    }
                }

                // ─── 4. MAĞAZA FLAPPING: Cihazlarin cogu aralikli kesinti ───
                var deviceFlapCounts = storeChanges
                    .Where(c => !c.DeviceType.Equals("ROUTER", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(c => c.DeviceId)
                    .Select(g => new { DeviceId = g.Key, Count = g.Count() })
                    .ToList();

                var flappingDevices = deviceFlapCounts
                    .Where(f => f.Count >= flappingThreshold)
                    .ToList();

                if (flappingDevices.Count > 1 && flappingDevices.Count >= nonRouterDevices.Count / 2)
                {
                    // Cihazlarin yarısindan fazlasi flapping → magaza geneli sorun
                    var totalFlaps = flappingDevices.Sum(f => f.Count);
                    diagnostics.Add(new StoreDiagnostic
                    {
                        StoreCode = storeCode,
                        StoreName = storeName,
                        Type = DiagnosticType.StoreFlapping,
                        Severity = DiagnosticSeverity.Warning,
                        Title = "Aralıklı Kesinti",
                        Message = $"{flappingDevices.Count} cihaz son {flappingWindowMinutes} dk icinde toplam {totalFlaps} kez durum degistirdi. Kararsız internet baglantisi olabilir.",
                        DetectedAt = now,
                        RouterOnline = routerOnline,
                        TotalDevices = nonRouterDevices.Count,
                        OnlineDevices = onlineNonRouter,
                        OfflineDevices = offlineNonRouter,
                        FlappingCount = totalFlaps,
                        AffectedDevices = flappingDevices
                            .Select(f => nonRouterDevices.FirstOrDefault(d => d.DeviceId == f.DeviceId)?.DeviceName ?? f.DeviceId)
                            .ToList()
                    });
                    continue; // Magaza geneli sorun varsa tekil device flapping ekleme
                }

                // ─── 5. TEKİL CİHAZ FLAPPING ───
                foreach (var flap in flappingDevices)
                {
                    var device = nonRouterDevices.FirstOrDefault(d => d.DeviceId == flap.DeviceId);
                    if (device == null) continue;

                    diagnostics.Add(new StoreDiagnostic
                    {
                        StoreCode = storeCode,
                        StoreName = storeName,
                        Type = DiagnosticType.DeviceFlapping,
                        Severity = DiagnosticSeverity.Warning,
                        Title = "Cihaz Kararsız",
                        Message = $"{device.DeviceName} son {flappingWindowMinutes} dk icinde {flap.Count} kez durum degistirdi.",
                        DetectedAt = now,
                        RouterOnline = routerOnline,
                        TotalDevices = nonRouterDevices.Count,
                        OnlineDevices = onlineNonRouter,
                        OfflineDevices = offlineNonRouter,
                        FlappingCount = flap.Count,
                        AffectedDevices = new List<string> { device.DeviceName }
                    });
                }

                // ─── 6. KISMİ KESİNTİ: Bazi cihazlar offline (flapping degil) ───
                if (routerOnline && offlineNonRouter > 0 && onlineNonRouter > 0
                    && !flappingDevices.Any()) // flapping zaten yukarida yakalandi
                {
                    var offlineList = nonRouterDevices.Where(d => !d.IsOnline).ToList();
                    diagnostics.Add(new StoreDiagnostic
                    {
                        StoreCode = storeCode,
                        StoreName = storeName,
                        Type = DiagnosticType.PartialOutage,
                        Severity = DiagnosticSeverity.Warning,
                        Title = "Kısmi Kesinti",
                        Message = $"{offlineNonRouter}/{nonRouterDevices.Count} cihaz offline. Cihaz bazlı sorun olabilir.",
                        DetectedAt = now,
                        RouterOnline = true,
                        TotalDevices = nonRouterDevices.Count,
                        OnlineDevices = onlineNonRouter,
                        OfflineDevices = offlineNonRouter,
                        AffectedDevices = offlineList.Select(d => d.DeviceName).ToList()
                    });
                }
            }

            // Oncelik sirasi: Critical > Warning > Info, ayni seviyede offline cihaz sayısına gore
            return diagnostics
                .OrderBy(d => d.Severity)
                .ThenByDescending(d => d.OfflineDevices)
                .ThenBy(d => d.StoreCode)
                .ToList();
        }

        /// <summary>
        /// Belirli bir magazanin gecmis durum gecislerini dondurur (zaman serisi)
        /// </summary>
        public async Task<object> GetStoreTimelineAsync(int storeCode, int hours = 24)
        {
            var since = DateTime.UtcNow.AddHours(-hours);

            var changes = await _db.DeviceStatusChanges
                .AsNoTracking()
                .Where(c => c.StoreCode == storeCode && c.ChangedAt >= since)
                .OrderBy(c => c.ChangedAt)
                .Select(c => new
                {
                    c.DeviceId,
                    c.DeviceType,
                    c.IsOnline,
                    c.ChangedAt
                })
                .ToListAsync();

            // Cihaz bazli gruplama
            var timeline = changes
                .GroupBy(c => new { c.DeviceId, c.DeviceType })
                .Select(g => new
                {
                    DeviceId = g.Key.DeviceId,
                    DeviceType = g.Key.DeviceType,
                    Changes = g.Select(c => new { c.IsOnline, c.ChangedAt }).ToList(),
                    TotalChanges = g.Count(),
                    LastStatus = g.OrderByDescending(c => c.ChangedAt).First().IsOnline
                })
                .OrderBy(d => d.DeviceType)
                .ToList();

            // Ozet istatistikler
            var summary = new
            {
                StoreCode = storeCode,
                PeriodHours = hours,
                TotalEvents = changes.Count,
                DeviceCount = timeline.Count,
                RouterEvents = changes.Count(c => c.DeviceType.Equals("ROUTER", StringComparison.OrdinalIgnoreCase)),
                PcEvents = changes.Count(c => c.DeviceType.Equals("PC", StringComparison.OrdinalIgnoreCase)),
                KasaEvents = changes.Count(c => c.DeviceType.StartsWith("KASA", StringComparison.OrdinalIgnoreCase)),
            };

            return new { summary, timeline };
        }

        /// <summary>
        /// Eski durum gecis kayitlarini temizle (varsayılan 30 gun)
        /// </summary>
        public async Task<int> PurgeOldChangesAsync(int retainDays = 30)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retainDays);
            var count = await _db.DeviceStatusChanges
                .Where(c => c.ChangedAt < cutoff)
                .ExecuteDeleteAsync();
            return count;
        }
    }
}
