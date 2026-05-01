using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Services
{
    public enum RouterLineClass
    {
        Unknown,         // yeterli veri yok
        Terrestrial,     // karasal (fiber/bakir) — dusuk latency, stabil
        MobileSuspected, // mobil hat suphesi (yukselmis latency)
        MobileLikely,    // mobil hat yuksek ihtimal (yuksek latency + jitter)
        Unstable         // basarisizlik orani yuksek (hat kesinti)
    }

    /// <summary>
    /// Router ping latency ornekleri uzerinden her magazanin
    /// karasal vs 4.5G mobil yedek hat durumunu siniflandirir.
    /// </summary>
    public class MobileLineDetectionService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        // Esikler — canlida kalibrasyondan sonra ayarlanacak
        private const int TerrestrialP50MaxMs = 120;
        private const int TerrestrialP95MaxMs = 200;
        private const int MobileSuspectedP50MinMs = 150;
        private const int MobileSuspectedP95MinMs = 300;
        private const int MobileLikelyP50MinMs = 250;
        private const int MobileLikelyP95MinMs = 400;
        private const double MobileLikelyStdDevMinMs = 80;
        private const double UnstableSuccessRateMax = 0.85;

        public MobileLineDetectionService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Son N dakikadaki ornekleri analiz eder ve her router icin sinif + metrik uretir.
        /// </summary>
        public async Task<List<RouterClassificationDto>> ClassifyAllAsync(int windowMinutes = 10, CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            var since = DateTime.UtcNow.AddMinutes(-windowMinutes);

            // Son pencere: tum ornekler
            var recent = await db.RouterLatencySamples
                .AsNoTracking()
                .Where(s => s.SampledAt >= since)
                .ToListAsync(ct);

            // Onceki pencere (switchover karsilastirmasi icin)
            var priorFrom = since.AddMinutes(-windowMinutes);
            var prior = await db.RouterLatencySamples
                .AsNoTracking()
                .Where(s => s.SampledAt >= priorFrom && s.SampledAt < since)
                .ToListAsync(ct);

            // Router cihaz meta (ad/IP icin)
            var routerDevices = await db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "Router" || d.DeviceType == "ROUTER" || d.DeviceType == "router")
                .ToListAsync(ct);

            // Karasal hat referans verisi
            var networkInfos = await db.StoreNetworkInfos.AsNoTracking().ToDictionaryAsync(s => s.StoreCode, ct);

            var results = new List<RouterClassificationDto>();

            foreach (var router in routerDevices)
            {
                var samples = recent.Where(s => s.DeviceId == router.DeviceId).ToList();
                var prevSamples = prior.Where(s => s.DeviceId == router.DeviceId).ToList();

                networkInfos.TryGetValue(router.StoreCode, out var info);

                var dto = new RouterClassificationDto
                {
                    DeviceId = router.DeviceId,
                    StoreCode = router.StoreCode,
                    StoreName = router.StoreName,
                    Ip = router.CalculatedIpAddress,
                    SampleCount = samples.Count,
                    TerrestrialMbps = info?.TerrestrialMbps,
                    LineType = info?.LineType
                };

                if (samples.Count == 0)
                {
                    dto.Class = RouterLineClass.Unknown;
                    dto.Reason = "Pencerede ornek yok";
                    results.Add(dto);
                    continue;
                }

                // Success rate: ICMP + TCP fallback dahil (reachability)
                var successCount = samples.Count(s => s.Success);
                var successRate = (double)successCount / samples.Count;
                dto.SuccessRate = Math.Round(successRate, 3);

                // Latency analizi SADECE gercek ICMP olculen orneklerden
                // (TCP fallback Success=true ama RttMs=null — handshake ping'i yansitmaz)
                var icmpSamples = samples.Where(s => s.Success && s.RttMs.HasValue).ToList();

                if (icmpSamples.Count == 0)
                {
                    if (successCount == 0)
                    {
                        dto.Class = RouterLineClass.Unstable;
                        dto.Reason = "Pencerede hic basarili ping yok";
                    }
                    else
                    {
                        // TCP fallback ile online ama ICMP hic cevap vermedi — muhtemelen ICMP bloke
                        dto.Class = RouterLineClass.Unknown;
                        dto.Reason = "ICMP cevabi yok, sadece TCP fallback";
                    }
                    results.Add(dto);
                    continue;
                }

                var rtts = icmpSamples.Select(s => (double)s.RttMs!.Value).OrderBy(r => r).ToList();
                dto.AvgRttMs = (int)rtts.Average();
                dto.P50Ms = Percentile(rtts, 0.50);
                dto.P95Ms = Percentile(rtts, 0.95);
                dto.StdDevMs = Math.Round(StdDev(rtts), 1);
                dto.LastSampleAt = samples.Max(s => s.SampledAt);

                // Switchover tespiti — onceki 10dk'ya gore sicrama
                if (prevSamples.Count > 0)
                {
                    var prevSuccessful = prevSamples.Where(s => s.Success && s.RttMs.HasValue).Select(s => (double)s.RttMs!.Value).ToList();
                    if (prevSuccessful.Count > 0)
                    {
                        var prevAvg = prevSuccessful.Average();
                        dto.PrevAvgRttMs = (int)prevAvg;
                        if (dto.AvgRttMs.HasValue && dto.AvgRttMs.Value - prevAvg > 100)
                            dto.SwitchoverDetected = true;
                    }
                }

                // Siniflandirma
                if (successRate < UnstableSuccessRateMax)
                {
                    dto.Class = RouterLineClass.Unstable;
                    dto.Reason = $"Basari orani dusuk ({successRate:P0})";
                }
                else if (dto.P50Ms >= MobileLikelyP50MinMs || (dto.P95Ms >= MobileLikelyP95MinMs && dto.StdDevMs > MobileLikelyStdDevMinMs))
                {
                    dto.Class = RouterLineClass.MobileLikely;
                    dto.Reason = $"Yuksek latency + jitter (p50={dto.P50Ms:F0}ms, p95={dto.P95Ms:F0}ms, std={dto.StdDevMs:F0}ms)";
                }
                else if (dto.P50Ms >= MobileSuspectedP50MinMs || dto.P95Ms >= MobileSuspectedP95MinMs)
                {
                    dto.Class = RouterLineClass.MobileSuspected;
                    dto.Reason = $"Yukselmis latency (p50={dto.P50Ms:F0}ms, p95={dto.P95Ms:F0}ms)";
                }
                else if (dto.P50Ms <= TerrestrialP50MaxMs && dto.P95Ms <= TerrestrialP95MaxMs)
                {
                    dto.Class = RouterLineClass.Terrestrial;
                    dto.Reason = $"Dusuk ve stabil latency (p50={dto.P50Ms:F0}ms, p95={dto.P95Ms:F0}ms)";
                }
                else
                {
                    dto.Class = RouterLineClass.Unknown;
                    dto.Reason = $"Araliksal degerler (p50={dto.P50Ms:F0}ms, p95={dto.P95Ms:F0}ms)";
                }

                results.Add(dto);
            }

            return results;
        }

        /// <summary>Router'in RTT zaman serisini doner (chart icin).</summary>
        public async Task<List<RouterLatencyPoint>> GetHistoryAsync(int storeCode, int hours = 24, CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            var since = DateTime.UtcNow.AddHours(-hours);
            return await db.RouterLatencySamples
                .AsNoTracking()
                .Where(s => s.StoreCode == storeCode && s.SampledAt >= since)
                .OrderBy(s => s.SampledAt)
                .Select(s => new RouterLatencyPoint
                {
                    SampledAt = s.SampledAt,
                    RttMs = s.RttMs,
                    Success = s.Success
                })
                .ToListAsync(ct);
        }

        /// <summary>7 gunden eski ornekleri siler.</summary>
        public async Task<int> PurgeOldSamplesAsync(int retainDays = 7, CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            var cutoff = DateTime.UtcNow.AddDays(-retainDays);
            return await db.RouterLatencySamples
                .Where(s => s.SampledAt < cutoff)
                .ExecuteDeleteAsync(ct);
        }

        private static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            if (sorted.Count == 1) return sorted[0];
            var rank = p * (sorted.Count - 1);
            var lo = (int)Math.Floor(rank);
            var hi = (int)Math.Ceiling(rank);
            if (lo == hi) return sorted[lo];
            var frac = rank - lo;
            return sorted[lo] * (1 - frac) + sorted[hi] * frac;
        }

        private static double StdDev(List<double> values)
        {
            if (values.Count < 2) return 0;
            var mean = values.Average();
            var sumSq = values.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(sumSq / values.Count);
        }
    }

    public class RouterClassificationDto
    {
        public string DeviceId { get; set; } = "";
        public int StoreCode { get; set; }
        public string StoreName { get; set; } = "";
        public string Ip { get; set; } = "";
        public RouterLineClass Class { get; set; }
        public string Reason { get; set; } = "";
        public int SampleCount { get; set; }
        public double SuccessRate { get; set; }
        public int? AvgRttMs { get; set; }
        public double P50Ms { get; set; }
        public double P95Ms { get; set; }
        public double StdDevMs { get; set; }
        public DateTime? LastSampleAt { get; set; }
        public int? PrevAvgRttMs { get; set; }
        public bool SwitchoverDetected { get; set; }

        // Karasal hat referans bilgisi
        public int? TerrestrialMbps { get; set; }
        public string? LineType { get; set; }
    }

    public class RouterLatencyPoint
    {
        public DateTime SampledAt { get; set; }
        public int? RttMs { get; set; }
        public bool Success { get; set; }
    }
}
