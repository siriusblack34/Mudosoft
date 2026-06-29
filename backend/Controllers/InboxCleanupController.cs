using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Middleware;
using Orchestra.Backend.Services;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [RequireMenu("/cleanup")] // Temizlik Merkezi menüsüne bağlı
    [Route("api/inbox-cleanup")]
    public class InboxCleanupController : ControllerBase
    {
        private readonly OrchestraDbContext _db;
        private readonly ILogger<InboxCleanupController> _logger;
        private readonly ActivityLogService _activity;

        private static readonly ConcurrentDictionary<string, CleanAllJob> _cleanAllJobs = new();
        private static readonly ConcurrentDictionary<string, CheckAllJob> _checkAllJobs = new();

        private const string READY_FOLDER = @"GeniusOpen\Inbox\000\Ready";
        private const string KASA_FOLDER = @"GeniusOpen\Kasa";
        private const string PROCESSED_FOLDER = @"GeniusOpen\Inbox\000\Ready\processed";
        private const string SEQ_FOLDER = @"GeniusOpen\Inbox\000\Seq";

        public InboxCleanupController(
            OrchestraDbContext db,
            ILogger<InboxCleanupController> logger,
            ActivityLogService activity)
        {
            _db = db;
            _logger = logger;
            _activity = activity;
        }

        public class InboxStatusDto
        {
            public string DeviceId { get; set; } = "";
            public int StoreCode { get; set; }
            public string StoreName { get; set; } = "";
            public string IpAddress { get; set; } = "";
            public string DeviceType { get; set; } = "";
            public bool IsOnline { get; set; }
            public int RdyCount { get; set; }
            public int TxtCount { get; set; }
            public int Tmp1Count { get; set; } // Kasa
            public int Tmp2Count { get; set; } // Ready
            public int DisCount { get; set; }  // Ready (.dis)
            public int ProCount { get; set; }  // Processed
            public int SeqCount { get; set; }  // Seq
            public int TotalCount { get; set; }
            public string Status { get; set; } = "unknown";
            public string? ErrorMessage { get; set; }
        }

        private static string GetUncPath(string ip) => $@"\\{ip}\C$\{READY_FOLDER}";
        private static string GetKasaPath(string ip) => $@"\\{ip}\C$\{KASA_FOLDER}";
        private static string GetProcessedPath(string ip) => $@"\\{ip}\C$\{PROCESSED_FOLDER}";
        private static string GetSeqPath(string ip) => $@"\\{ip}\C$\{SEQ_FOLDER}";

        /// <summary>
        /// Tüm portları paralel kontrol eder. (online, smbOpen) döner.
        /// smbOpen=true → C$ UNC erişimi mümkün demektir.
        /// </summary>
        private static async Task<(bool online, bool smbOpen)> IsDeviceOnlineAsync(
            string ip, int timeoutMs = 2000, CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            var token = cts.Token;

            var sqlTask = TryTcpConnectAsync(ip, 1433, token);
            var smbTask = TryTcpConnectAsync(ip, 445, token);
            var pingTask = PingAsync(ip, Math.Max(500, timeoutMs / 3));

            try { await Task.WhenAll(sqlTask, smbTask, pingTask); } catch { }

            var smbOpen = smbTask.IsCompletedSuccessfully && smbTask.Result;
            var online = smbOpen
                || (sqlTask.IsCompletedSuccessfully && sqlTask.Result)
                || (pingTask.IsCompletedSuccessfully && pingTask.Result);

            return (online, smbOpen);
        }

        private static async Task<bool> TryTcpConnectAsync(string ip, int port, CancellationToken ct)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(ip, port, ct);
                return client.Connected;
            }
            catch { return false; }
        }

        private static async Task<bool> PingAsync(string ip, int timeoutMs)
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(ip, timeoutMs);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch { return false; }
        }

        private static async Task<(int rdy, int txt, int tmp, int dis, string? error)> GetFileCountsAsync(
            string uncPath, int timeoutSec, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            try
            {
                return await Task.Run(() =>
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (!Directory.Exists(uncPath))
                        return (0, 0, 0, 0, (string?)"Klasör bulunamadı");

                    cts.Token.ThrowIfCancellationRequested();
                    var rdyFiles = Directory.GetFiles(uncPath, "*.rdy");
                    cts.Token.ThrowIfCancellationRequested();
                    var txtFiles = Directory.GetFiles(uncPath, "*.txt");
                    cts.Token.ThrowIfCancellationRequested();
                    var tmpFiles = Directory.GetFiles(uncPath, "*.tmp");
                    cts.Token.ThrowIfCancellationRequested();
                    var disFiles = Directory.GetFiles(uncPath, "*.dis");
                    return (rdyFiles.Length, txtFiles.Length, tmpFiles.Length, disFiles.Length, (string?)null);
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return (0, 0, 0, 0, ct.IsCancellationRequested ? "İptal edildi" : "Zaman aşımı");
            }
            catch (Exception ex)
            {
                return (0, 0, 0, 0, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
        }

        private static async Task<(int count, string? error)> GetKasaCountsAsync(
            string uncPath, int timeoutSec, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            try
            {
                return await Task.Run(() =>
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (!Directory.Exists(uncPath))
                        return (0, (string?)"Klasör bulunamadı");
                    cts.Token.ThrowIfCancellationRequested();
                    return (Directory.GetFiles(uncPath, "*.tmp").Length, (string?)null);
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return (0, ct.IsCancellationRequested ? "İptal edildi" : "Zaman aşımı");
            }
            catch (Exception ex)
            {
                return (0, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
        }

        private static async Task<(int count, string? error)> GetProcessedCountsAsync(
            string uncPath, int timeoutSec, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            try
            {
                return await Task.Run(() =>
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (!Directory.Exists(uncPath))
                        return (0, (string?)null);
                    cts.Token.ThrowIfCancellationRequested();
                    var rdy = Directory.GetFiles(uncPath, "*.rdy").Length;
                    cts.Token.ThrowIfCancellationRequested();
                    var txt = Directory.GetFiles(uncPath, "*.txt").Length;
                    return (rdy + txt, (string?)null);
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return (0, ct.IsCancellationRequested ? "İptal edildi" : "Zaman aşımı");
            }
            catch (Exception ex)
            {
                if (ex is DirectoryNotFoundException) return (0, null);
                return (0, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
        }

        private static async Task<(int count, string? error)> GetSeqCountsAsync(
            string uncPath, int timeoutSec, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            try
            {
                return await Task.Run(() =>
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (!Directory.Exists(uncPath))
                        return (0, (string?)null);
                    cts.Token.ThrowIfCancellationRequested();
                    var rdy = Directory.GetFiles(uncPath, "*.rdy").Length;
                    cts.Token.ThrowIfCancellationRequested();
                    var txt = Directory.GetFiles(uncPath, "*.txt").Length;
                    cts.Token.ThrowIfCancellationRequested();
                    var tmp = Directory.GetFiles(uncPath, "*.tmp").Length;
                    return (rdy + txt + tmp, (string?)null);
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return (0, ct.IsCancellationRequested ? "İptal edildi" : "Zaman aşımı");
            }
            catch (Exception ex)
            {
                if (ex is DirectoryNotFoundException) return (0, null);
                return (0, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
        }

        // ===========================================================
        // 1) TÜM PC'LERİ KONTROL ET — JOB-BASED
        // ===========================================================
        [HttpPost("check-all")]
        public async Task<IActionResult> CheckAll([FromServices] IServiceScopeFactory scopeFactory, CancellationToken ct)
        {
            var pcCount = await _db.StoreDevices
                .AsNoTracking()
                .CountAsync(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici", ct);

            // Eski tamamlanmış job'ları temizle
            var stale = _checkAllJobs
                .Where(kvp => kvp.Value.CompletedAtUtc.HasValue &&
                    (DateTime.UtcNow - kvp.Value.CompletedAtUtc.Value).TotalHours > 1)
                .Select(kvp => kvp.Key).ToList();
            foreach (var k in stale) _checkAllJobs.TryRemove(k, out _);

            var jobId = Guid.NewGuid().ToString("N")[..8];
            var job = new CheckAllJob { JobId = jobId, TotalCount = pcCount, StartedAtUtc = DateTime.UtcNow };
            _checkAllJobs[jobId] = job;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

                    var pcDevices = await db.StoreDevices
                        .AsNoTracking()
                        .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici")
                        .ToListAsync();

                    using var sem = new SemaphoreSlim(10);
                    var tasks = pcDevices.Select(async device =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            var dto = await CheckDeviceAsync(device, CancellationToken.None);
                            lock (job.Lock) { job.Results.Add(dto); job.CompletedCount = job.Results.Count; }
                        }
                        finally { sem.Release(); }
                    });

                    await Task.WhenAll(tasks);
                    lock (job.Lock) { job.CompletedAtUtc = DateTime.UtcNow; }
                }
                catch (Exception ex)
                {
                    lock (job.Lock) { job.Error = ex.Message; job.CompletedAtUtc = DateTime.UtcNow; }
                }
            });

            _logger.LogInformation("Inbox check-all job {JobId} started for {Count} devices", jobId, pcCount);
            return Accepted(new { jobId, totalCount = pcCount });
        }

        // ===========================================================
        // 1b) KONTROL JOB DURUMU
        // ===========================================================
        [HttpGet("check-all/{jobId}")]
        public IActionResult GetCheckAllStatus(string jobId)
        {
            if (!_checkAllJobs.TryGetValue(jobId, out var job))
                return NotFound(new { error = "Job bulunamadi" });

            lock (job.Lock)
            {
                return Ok(new
                {
                    jobId = job.JobId,
                    totalCount = job.TotalCount,
                    completedCount = job.CompletedCount,
                    isCompleted = job.CompletedAtUtc.HasValue,
                    error = job.Error,
                    results = job.Results.OrderBy(r => r.StoreCode).ToList()
                });
            }
        }

        /// <summary>
        /// Tek cihazı kontrol et. SMB porta bakarak UNC erişimini önceden doğrular.
        /// </summary>
        private static async Task<InboxStatusDto> CheckDeviceAsync(
            Orchestra.Backend.Models.StoreDevice device, CancellationToken ct)
        {
            var dto = new InboxStatusDto
            {
                DeviceId = device.DeviceId,
                StoreCode = device.StoreCode,
                StoreName = device.StoreName,
                IpAddress = device.CalculatedIpAddress,
                DeviceType = device.DeviceType
            };

            var (isOnline, smbOpen) = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000, ct);
            dto.IsOnline = isOnline;

            if (!isOnline)
            {
                dto.Status = "offline";
                return dto;
            }

            // Port 445 (SMB) kapalıysa UNC erişimi mümkün değil — thread'i bloklama
            if (!smbOpen)
            {
                dto.Status = "error";
                dto.ErrorMessage = "SMB (port 445) kapalı — C$ erişimi mümkün değil";
                return dto;
            }

            var ip = device.CalculatedIpAddress;

            // Sıralı tara: dolu/parçalı disklerde 4 eş zamanlı UNC isteği disk'i daha da yavaşlatır.
            // Sıralı → daha az disk baskısı, daha güvenilir sonuç.
            var (rdy, txt, tmp2, dis, errorInbox) = await GetFileCountsAsync(GetUncPath(ip), 20, ct);
            var (tmp1, errorKasa) = await GetKasaCountsAsync(GetKasaPath(ip), 40, ct);
            var (pro, errorPro) = await GetProcessedCountsAsync(GetProcessedPath(ip), 90, ct);
            var (seq, errorSeq) = await GetSeqCountsAsync(GetSeqPath(ip), 20, ct);

            // En az bir gerçek hata varsa (boş klasör hataları değil) — error durumu
            var errors = new[] { errorInbox, errorKasa, errorPro, errorSeq }
                .Where(e => e != null && e != "Klasör bulunamadı")
                .ToArray();

            if (errors.Length > 0)
            {
                dto.Status = "error";
                dto.ErrorMessage = string.Join(" | ", errors);
            }
            else
            {
                dto.RdyCount = rdy;
                dto.TxtCount = txt;
                dto.Tmp1Count = tmp1;
                dto.Tmp2Count = tmp2;
                dto.DisCount = dis;
                dto.ProCount = pro;
                dto.SeqCount = seq;
                dto.TotalCount = rdy + txt + tmp1 + tmp2 + dis + pro + seq;
                dto.Status = dto.TotalCount > 0 ? "dirty" : "clean";
            }

            return dto;
        }

        // ===========================================================
        // 2) TEK PC KONTROL
        // ===========================================================
        [HttpGet("check/{deviceId}")]
        public async Task<IActionResult> CheckSingle(string deviceId, CancellationToken ct)
        {
            var device = await _db.StoreDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);

            if (device == null)
                return NotFound(new { error = "Device not found" });

            var dto = await CheckDeviceAsync(device, ct);
            return Ok(dto);
        }

        // ===========================================================
        // 3) TEK PC TEMİZLE
        // ===========================================================
        [HttpPost("clean/{deviceId}")]
        public async Task<IActionResult> CleanSingle(string deviceId, CancellationToken ct)
        {
            var device = await _db.StoreDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);

            if (device == null)
                return NotFound(new { error = "Device not found" });

            var (isOnline, smbOpen) = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000, ct);
            if (!isOnline)
                return BadRequest(new { error = "Cihaz offline" });
            if (!smbOpen)
                return BadRequest(new { error = "SMB (port 445) erişilemiyor — C$ paylaşımı ulaşılamıyor" });

            var ip = device.CalculatedIpAddress;

            // Ready: direct delete (has processed/ subdir — renaming Ready would break Genius)
            var (del1, err1) = await DeleteFilesAsync(GetUncPath(ip), new[] { "*.rdy", "*.txt", "*.tmp", "*.dis" }, 120, ct);
            // processed: rename trick (leaf dir, can accumulate 5000+ files)
            var (del2, err2) = await RenameDirectoryAsync(GetProcessedPath(ip));
            // Kasa + Seq: rename trick (leaf dirs, safe to rename)
            var (del3, err3) = await RenameDirectoryAsync(GetKasaPath(ip));
            var (del4, err4) = await RenameDirectoryAsync(GetSeqPath(ip));

            var directDeleted = del1;
            var renamedCount = (del2 > 0 ? 1 : 0) + (del3 > 0 ? 1 : 0) + (del4 > 0 ? 1 : 0);
            var anyError = err1 ?? err2 ?? err3 ?? err4;

            if (directDeleted == 0 && renamedCount == 0 && anyError != null)
                return BadRequest(new { error = anyError });

            var parts = new List<string>();
            if (directDeleted > 0) parts.Add($"{directDeleted} dosya");
            if (renamedCount > 0) parts.Add($"{renamedCount} klasör yenilendi");
            var reason = parts.Count > 0 ? string.Join(", ", parts) : "zaten temiz";
            _logger.LogInformation("Inbox cleaned: {DeviceId} ({Ip}) - {Count} direkt, {Renamed} klasör yenilendi", device.DeviceId, ip, directDeleted, renamedCount);
            await _activity.LogAsync("Cleanup", "InboxCleanSingle", $"{device.StoreName} ({device.DeviceId})", reason, ct: ct);
            return Ok(new { success = true, deleted = directDeleted + renamedCount, message = $"{device.StoreName} temizlendi ({reason})" });
        }

        private static async Task<(int deleted, string? error)> RenameDirectoryAsync(string uncPath)
        {
            return await Task.Factory.StartNew(() =>
            {
                try
                {
                    if (!Directory.Exists(uncPath)) return (0, (string?)null);

                    bool hasContent = Directory.EnumerateFileSystemEntries(uncPath).Any();
                    if (!hasContent) return (0, null);

                    int deleted = 0;
                    foreach (var file in Directory.GetFiles(uncPath, "*", SearchOption.TopDirectoryOnly))
                    {
                        try { System.IO.File.Delete(file); deleted++; } catch { }
                    }
                    foreach (var dir in Directory.GetDirectories(uncPath))
                    {
                        try { Directory.Delete(dir, true); deleted++; } catch { }
                    }
                    return (deleted, null);
                }
                catch (Exception ex)
                {
                    return (0, ex.Message.Length > 150 ? ex.Message[..150] : ex.Message);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        // ===========================================================
        // 4) TÜM ONLINE PC'LERİ TEMİZLE — JOB-BASED
        // ===========================================================
        [HttpPost("clean-all")]
        public async Task<IActionResult> CleanAll(
            [FromServices] IInboxCleanupService cleanupService,
            [FromServices] IServiceScopeFactory scopeFactory)
        {
            var pcCount = await _db.StoreDevices
                .AsNoTracking()
                .CountAsync(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici");

            var jobId = Guid.NewGuid().ToString("N")[..8];
            var job = new CleanAllJob { JobId = jobId, TotalCount = pcCount, StartedAtUtc = DateTime.UtcNow };
            _cleanAllJobs[jobId] = job;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IInboxCleanupService>();
                    var progress = new Progress<CleanResultItem>(item =>
                    {
                        lock (job.Lock)
                        {
                            job.Results.Add(item);
                            job.CompletedCount = job.Results.Count;
                            if (item.Success) job.SuccessCount++;
                            else if (item.Reason == "offline") job.OfflineCount++;
                            else job.ErrorCount++;
                            job.LastDeviceName = item.StoreName;
                        }
                    });

                    var (success, total, _) = await svc.CleanAllAsync(progress, CancellationToken.None);
                    lock (job.Lock)
                    {
                        job.SuccessCount = success;
                        job.TotalCount = total;
                        job.CompletedAtUtc = DateTime.UtcNow;
                    }
                    _logger.LogInformation("Clean-all job {JobId} tamamlandi: {Success}/{Total}", jobId, success, total);
                    using var s2 = scopeFactory.CreateScope();
                    var act = s2.ServiceProvider.GetRequiredService<ActivityLogService>();
                    await act.LogAsync("Cleanup", "InboxCleanAll", jobId, $"{success}/{total} basarili");
                }
                catch (Exception ex)
                {
                    lock (job.Lock) { job.Error = ex.Message; job.CompletedAtUtc = DateTime.UtcNow; }
                    _logger.LogError(ex, "Clean-all job {JobId} hata", jobId);
                    using var s2 = scopeFactory.CreateScope();
                    var act = s2.ServiceProvider.GetRequiredService<ActivityLogService>();
                    await act.LogAsync("Cleanup", "InboxCleanAll", jobId, null, false, ex.Message);
                }
            });

            await _activity.LogAsync("Cleanup", "InboxCleanAllStarted", jobId, $"{pcCount} PC kuyruga alindi");
            return Accepted(new { jobId, totalCount = pcCount });
        }

        // ===========================================================
        // 5) TOPLU TEMİZLİK JOB DURUMU
        // ===========================================================
        [HttpGet("clean-all/{jobId}")]
        public IActionResult GetCleanAllStatus(string jobId)
        {
            if (!_cleanAllJobs.TryGetValue(jobId, out var job))
                return NotFound(new { error = "Job bulunamadi" });

            lock (job.Lock)
            {
                return Ok(new
                {
                    jobId = job.JobId,
                    totalCount = job.TotalCount,
                    completedCount = job.CompletedCount,
                    successCount = job.SuccessCount,
                    offlineCount = job.OfflineCount,
                    errorCount = job.ErrorCount,
                    lastDeviceName = job.LastDeviceName,
                    startedAtUtc = job.StartedAtUtc.ToString("o"),
                    completedAtUtc = job.CompletedAtUtc?.ToString("o"),
                    isCompleted = job.CompletedAtUtc.HasValue,
                    error = job.Error,
                    results = job.CompletedAtUtc.HasValue ? job.Results : null
                });
            }
        }

        private sealed class CheckAllJob
        {
            public string JobId { get; init; } = "";
            public int TotalCount { get; set; }
            public int CompletedCount { get; set; }
            public List<InboxStatusDto> Results { get; } = new();
            public DateTime StartedAtUtc { get; set; }
            public DateTime? CompletedAtUtc { get; set; }
            public string? Error { get; set; }
            public object Lock { get; } = new();
        }

        private sealed class CleanAllJob
        {
            public string JobId { get; init; } = "";
            public int TotalCount { get; set; }
            public int CompletedCount { get; set; }
            public int SuccessCount { get; set; }
            public int OfflineCount { get; set; }
            public int ErrorCount { get; set; }
            public string? LastDeviceName { get; set; }
            public DateTime StartedAtUtc { get; set; }
            public DateTime? CompletedAtUtc { get; set; }
            public string? Error { get; set; }
            public List<CleanResultItem> Results { get; } = new();
            public object Lock { get; } = new();
        }

        private static async Task<(int deleted, string? error)> DeleteFilesAsync(
            string uncPath, string[] patterns, int timeoutSec, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            try
            {
                var count = await Task.Factory.StartNew(() =>
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (!Directory.Exists(uncPath)) return 0;

                    int deleted = 0;
                    foreach (var pattern in patterns)
                    {
                        if (cts.Token.IsCancellationRequested) break;
                        foreach (var f in Directory.EnumerateFiles(uncPath, pattern))
                        {
                            if (cts.Token.IsCancellationRequested) break;
                            try { System.IO.File.Delete(f); deleted++; } catch { }
                        }
                    }
                    return deleted;
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                return (count, null);
            }
            catch (OperationCanceledException)
            {
                return (0, ct.IsCancellationRequested ? "İptal edildi" : "Zaman aşımı");
            }
            catch (Exception ex)
            {
                if (ex is DirectoryNotFoundException) return (0, null);
                return (0, ex.Message.Length > 150 ? ex.Message[..150] : ex.Message);
            }
        }
    }
}
