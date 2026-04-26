using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Services;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace MudoSoft.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/inbox-cleanup")]
    public class InboxCleanupController : ControllerBase
    {
        private readonly MudoSoftDbContext _db;
        private readonly ILogger<InboxCleanupController> _logger;

        private static readonly ConcurrentDictionary<string, CleanAllJob> _cleanAllJobs = new();

        private const string READY_FOLDER = @"GeniusOpen\Inbox\000\Ready";
        private const string KASA_FOLDER = @"GeniusOpen\Kasa";
        private const string PROCESSED_FOLDER = @"GeniusOpen\Inbox\000\Ready\processed";
        private const string SEQ_FOLDER = @"GeniusOpen\Inbox\000\Seq";

        public InboxCleanupController(
            MudoSoftDbContext db,
            ILogger<InboxCleanupController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // DTO
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
            public int DisCount { get; set; } // Ready (.dis)
            public int ProCount { get; set; } // Processed
            public int SeqCount { get; set; } // Seq
            public int TotalCount { get; set; }
            public string Status { get; set; } = "unknown";
            public string? ErrorMessage { get; set; }
        }

        private string GetUncPath(string ip)
        {
            return $@"\\{ip}\C$\{READY_FOLDER}";
        }

        private string GetKasaPath(string ip)
        {
            return $@"\\{ip}\C$\{KASA_FOLDER}";
        }

        private string GetProcessedPath(string ip)
        {
            return $@"\\{ip}\C$\{PROCESSED_FOLDER}";
        }

        private string GetSeqPath(string ip)
        {
            return $@"\\{ip}\C$\{SEQ_FOLDER}";
        }

        /// <summary>
        /// Cihazın online olup olmadığını kontrol et: SQL(1433) -> SMB(445) -> Ping
        /// CancellationToken ile timeout olduğunda bağlantı temiz şekilde iptal edilir.
        /// </summary>
        private async Task<bool> IsDeviceOnlineAsync(string ip, int timeoutMs = 2000, CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            var token = cts.Token;

            // 1. SQL Port (1433)
            if (await TryTcpConnectAsync(ip, 1433, token))
                return true;

            // 2. SMB Port (445)
            if (await TryTcpConnectAsync(ip, 445, token))
                return true;

            // 3. Ping
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(ip, Math.Max(500, timeoutMs / 3));
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch { }

            return false;
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

        /// <summary>
        /// UNC path üzerinde dosya sayımı. CancellationToken ile iptal edilebilir.
        /// </summary>
        private async Task<(int rdy, int txt, int tmp, int dis, string? error)> GetFileCountsAsync(
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

        /// <summary>
        /// Kasa klasöründeki .tmp dosyalarını say
        /// </summary>
        private async Task<(int count, string? error)> GetKasaCountsAsync(
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
                    var tmpFiles = Directory.GetFiles(uncPath, "*.tmp");
                    return (tmpFiles.Length, (string?)null);
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

        private async Task<(int count, string? error)> GetProcessedCountsAsync(
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
                    var rdyFiles = Directory.GetFiles(uncPath, "*.rdy");
                    cts.Token.ThrowIfCancellationRequested();
                    var txtFiles = Directory.GetFiles(uncPath, "*.txt");
                    return (rdyFiles.Length + txtFiles.Length, (string?)null);
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

        private async Task<(int count, string? error)> GetSeqCountsAsync(
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
                    var rdyFiles = Directory.GetFiles(uncPath, "*.rdy");
                    cts.Token.ThrowIfCancellationRequested();
                    var txtFiles = Directory.GetFiles(uncPath, "*.txt");
                    cts.Token.ThrowIfCancellationRequested();
                    var tmpFiles = Directory.GetFiles(uncPath, "*.tmp");
                    return (rdyFiles.Length + txtFiles.Length + tmpFiles.Length, (string?)null);
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
        // 1) TÜM PC'LERİ KONTROL ET
        // ===========================================================
        [HttpPost("check-all")]
        public async Task<IActionResult> CheckAll(CancellationToken ct)
        {
            var pcDevices = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici")
                .ToListAsync(ct);

            _logger.LogInformation("Inbox check-all starting for {Count} devices (PC+Gecici)", pcDevices.Count);

            var results = new List<InboxStatusDto>();
            // 10 eşzamanlı cihaz — her biri 4 UNC scan yapıyor, toplam ~40 bloklayan I/O
            using var sem = new SemaphoreSlim(10);

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var dto = await CheckDeviceAsync(device, ct);
                    lock (results) results.Add(dto);
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);

            var ordered = results.OrderBy(r => r.StoreCode).ToList();
            _logger.LogInformation("Inbox check-all done: {Clean} clean, {Dirty} dirty, {Offline} offline, {Error} error",
                ordered.Count(r => r.Status == "clean"),
                ordered.Count(r => r.Status == "dirty"),
                ordered.Count(r => r.Status == "offline"),
                ordered.Count(r => r.Status == "error"));

            return Ok(ordered);
        }

        /// <summary>
        /// Tek cihazın inbox durumunu kontrol et (check-all ve check-single ortak kullanır)
        /// </summary>
        private async Task<InboxStatusDto> CheckDeviceAsync(
            MudoSoft.Backend.Models.StoreDevice device, CancellationToken ct)
        {
            var dto = new InboxStatusDto
            {
                DeviceId = device.DeviceId,
                StoreCode = device.StoreCode,
                StoreName = device.StoreName,
                IpAddress = device.CalculatedIpAddress,
                DeviceType = device.DeviceType
            };

            var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000, ct);
            dto.IsOnline = isOnline;

            if (!isOnline)
            {
                dto.Status = "offline";
                return dto;
            }

            var ip = device.CalculatedIpAddress;

            // 4 klasörü paralel tara (aynı cihaz içinde paralel — hepsi aynı IP)
            var inboxTask = GetFileCountsAsync(GetUncPath(ip), 15, ct);
            var kasaTask = GetKasaCountsAsync(GetKasaPath(ip), 15, ct);
            var proTask = GetProcessedCountsAsync(GetProcessedPath(ip), 30, ct);
            var seqTask = GetSeqCountsAsync(GetSeqPath(ip), 30, ct);

            await Task.WhenAll(inboxTask, kasaTask, proTask, seqTask);

            var (rdy, txt, tmp2, dis, errorInbox) = await inboxTask;
            var (tmp1, errorKasa) = await kasaTask;
            var (pro, errorProcessed) = await proTask;
            var (seq, errorSeq) = await seqTask;

            if (errorInbox != null || errorKasa != null || errorProcessed != null || errorSeq != null)
            {
                dto.Status = "error";
                dto.ErrorMessage = $"{errorInbox ?? ""} | {errorKasa ?? ""} | {errorProcessed ?? ""} | {errorSeq ?? ""}".Trim(' ', '|');
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

            var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000, ct);
            if (!isOnline)
                return BadRequest(new { error = "Device is offline" });

            var ip = device.CalculatedIpAddress;

            // 4 klasörü paralel temizle
            var t1 = DeleteFilesAsync(GetUncPath(ip), new[] { "*.rdy", "*.txt", "*.tmp", "*.dis" }, 10, ct);
            var t2 = DeleteFilesAsync(GetKasaPath(ip), new[] { "*.tmp" }, 10, ct);
            var t3 = DeleteFilesAsync(GetProcessedPath(ip), new[] { "*.rdy", "*.txt" }, 60, ct);
            var t4 = DeleteFilesAsync(GetSeqPath(ip), new[] { "*.rdy", "*.txt", "*.tmp" }, 60, ct);

            await Task.WhenAll(t1, t2, t3, t4);

            var (del1, err1) = await t1;
            var (del2, err2) = await t2;
            var (del3, err3) = await t3;
            var (del4, err4) = await t4;

            if (err1 != null && err2 != null && err3 != null && err4 != null)
                return BadRequest(new { error = $"{err1} | {err2} | {err3} | {err4}" });

            var totalDeleted = del1 + del2 + del3 + del4;
            _logger.LogInformation("Inbox cleaned: {DeviceId} ({Ip}) - {Count} files", device.DeviceId, ip, totalDeleted);
            return Ok(new { success = true, deleted = totalDeleted, message = $"{device.StoreName} temizlendi ({totalDeleted} dosya)" });
        }

        // ===========================================================
        // 4) TÜM ONLINE PC'LERİ TEMİZLE — JOB-BASED (async)
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
            var job = new CleanAllJob
            {
                JobId = jobId,
                TotalCount = pcCount,
                StartedAtUtc = DateTime.UtcNow
            };
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
                }
                catch (Exception ex)
                {
                    lock (job.Lock)
                    {
                        job.Error = ex.Message;
                        job.CompletedAtUtc = DateTime.UtcNow;
                    }
                    _logger.LogError(ex, "Clean-all job {JobId} hata", jobId);
                }
            });

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

        private async Task<(int deleted, string? error)> DeleteFilesAsync(
            string uncPath, string[] patterns, int timeoutSec, CancellationToken ct)
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

                    int count = 0;
                    foreach (var pattern in patterns)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        foreach (var f in Directory.GetFiles(uncPath, pattern))
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            try { System.IO.File.Delete(f); count++; } catch { }
                        }
                    }
                    return (count, (string?)null);
                }, cts.Token);
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
