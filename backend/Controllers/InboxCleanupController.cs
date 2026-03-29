using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Services;
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
        /// </summary>
        private async Task<bool> IsDeviceOnlineAsync(string ip, int timeoutMs = 2000)
        {
            // 1. SQL Port (1433) Kontrolü (En güvenilir)
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, 1433);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask && client.Connected)
                {
                    return true;
                }
            }
            catch { /* ignore */ }

            // 2. SMB Port (445) Kontrolü
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, 445);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask && client.Connected)
                {
                    return true;
                }
            }
            catch { /* ignore */ }

            // 3. Ping Kontrolü (Yedek)
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(ip, timeoutMs);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        /// <summary>
        /// Inbox (Ready) klasöründeki dosyaları say (.rdy, .txt, .tmp)
        /// </summary>
        private async Task<(int rdy, int txt, int tmp, string? error)> GetInboxCountsAsync(string uncPath, int timeoutSec = 20)
        {
            try
            {
                var task = Task.Run(() =>
                {
                    if (!Directory.Exists(uncPath))
                        return (0, 0, 0, (string?)"Klasör bulunamadı");

                    var rdyFiles = Directory.GetFiles(uncPath, "*.rdy");
                    var txtFiles = Directory.GetFiles(uncPath, "*.txt");
                    var tmpFiles = Directory.GetFiles(uncPath, "*.tmp");
                    return (rdyFiles.Length, txtFiles.Length, tmpFiles.Length, (string?)null);
                });

                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSec))) == task)
                    return await task;

                return (0, 0, 0, "Zaman aşımı");
            }
            catch (Exception ex)
            {
                return (0, 0, 0, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
        }

        /// <summary>
        /// Kasa klasöründeki dosyaları say (.tmp)
        /// </summary>
        private async Task<(int count, string? error)> GetKasaCountsAsync(string uncPath, int timeoutSec = 20)
        {
            try
            {
                var task = Task.Run(() =>
                {
                    if (!Directory.Exists(uncPath))
                        return (0, (string?)"Klasör bulunamadı");

                    var tmpFiles = Directory.GetFiles(uncPath, "*.tmp");
                    return (tmpFiles.Length, (string?)null);
                });

                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSec))) == task)
                    return await task;

                return (0, "Zaman aşımı");
            }
            catch (Exception ex)
            {
                return (0, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
        }

        private async Task<(int count, string? error)> GetProcessedCountsAsync(string uncPath, int timeoutSec = 20)
        {
            try
            {
                var task = Task.Run(() =>
                {
                    if (!Directory.Exists(uncPath))
                        return (0, (string?)"Klasör bulunamadı");

                    var rdyFiles = Directory.GetFiles(uncPath, "*.rdy");
                    var txtFiles = Directory.GetFiles(uncPath, "*.txt");
                    return (rdyFiles.Length + txtFiles.Length, (string?)null);
                });

                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSec))) == task)
                    return await task;

                return (0, "Zaman aşımı");
            }
            catch (Exception ex)
            {
                // Klasör yoksa hata dönme, 0 dön (processed klasörü olmayabilir)
                if (ex is DirectoryNotFoundException) return (0, null);
                return (0, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
        }

        private async Task<(int count, string? error)> GetSeqCountsAsync(string uncPath, int timeoutSec = 20)
        {
            try
            {
                var task = Task.Run(() =>
                {
                    if (!Directory.Exists(uncPath))
                        return (0, (string?)"Klasör bulunamadı");

                    var rdyFiles = Directory.GetFiles(uncPath, "*.rdy");
                    var txtFiles = Directory.GetFiles(uncPath, "*.txt");
                    var tmpFiles = Directory.GetFiles(uncPath, "*.tmp");
                    return (rdyFiles.Length + txtFiles.Length + tmpFiles.Length, (string?)null);
                });

                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSec))) == task)
                    return await task;

                return (0, "Zaman aşımı");
            }
            catch (Exception ex)
            {
                // Klasör yoksa hata dönme, 0 dön (seq klasörü olmayabilir)
                if (ex is DirectoryNotFoundException) return (0, null);
                return (0, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
        }

        private async Task<(int rdy, int txt, int tmp, int dis, string? error)> GetFileCountsAsync(string uncPath, int timeoutSec = 20)
        {
            try
            {
                var task = Task.Run(() =>
                {
                    if (!Directory.Exists(uncPath))
                        return (0, 0, 0, 0, (string?)"Klasör bulunamadı");

                    var rdyFiles = Directory.GetFiles(uncPath, "*.rdy");
                    var txtFiles = Directory.GetFiles(uncPath, "*.txt");
                    var tmpFiles = Directory.GetFiles(uncPath, "*.tmp");
                    var disFiles = Directory.GetFiles(uncPath, "*.dis");
                    return (rdyFiles.Length, txtFiles.Length, tmpFiles.Length, disFiles.Length, (string?)null);
                });

                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSec))) == task)
                    return await task;

                return (0, 0, 0, 0, "Zaman aşımı");
            }
            catch (Exception ex)
            {
                return (0, 0, 0, 0, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
        }



        // ===========================================================
        // 1) TÜM PC'LERİ KONTROL ET
        // ===========================================================
        [HttpPost("check-all")]
        public async Task<IActionResult> CheckAll()
        {
            var pcDevices = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici")
                .ToListAsync();

            _logger.LogInformation("Inbox check-all starting for {Count} devices (PC+Gecici)", pcDevices.Count);

            var results = new List<InboxStatusDto>();
            using var sem = new SemaphoreSlim(20); // network contention azaltmak için

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync();
                try
                {
                    var dto = new InboxStatusDto
                    {
                        DeviceId = device.DeviceId,
                        StoreCode = device.StoreCode,
                        StoreName = device.StoreName,
                        IpAddress = device.CalculatedIpAddress,
                        DeviceType = device.DeviceType
                    };

                    // Online kontrolü (SQL -> SMB -> Ping)
                    var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000);
                    dto.IsOnline = isOnline;

                    if (!isOnline)
                    {
                        dto.Status = "offline";
                        lock (results) results.Add(dto);
                        return;
                    }

                    // 1. Inbox (Ready) sayımı (rdy, txt, tmp2, dis)
                    var uncPathInbox = GetUncPath(device.CalculatedIpAddress);
                    var (rdy, txt, tmp2, dis, errorInbox) = await GetFileCountsAsync(uncPathInbox, 30);

                    // 2. Kasa sayımı (tmp1)
                    var uncPathKasa = GetKasaPath(device.CalculatedIpAddress);
                    var (tmp1, errorKasa) = await GetKasaCountsAsync(uncPathKasa, 30);

                    // 3. Processed sayımı (pro)
                    var uncPathProcessed = GetProcessedPath(device.CalculatedIpAddress);
                    var (pro, errorProcessed) = await GetProcessedCountsAsync(uncPathProcessed, 60);

                    // 4. Seq sayımı (seq)
                    var uncPathSeq = GetSeqPath(device.CalculatedIpAddress);
                    var (seq, errorSeq) = await GetSeqCountsAsync(uncPathSeq, 60);

                    if (errorInbox != null || errorKasa != null || errorProcessed != null || errorSeq != null)
                    {
                        dto.Status = "error";
                        dto.ErrorMessage = $"{errorInbox ?? ""} | {errorKasa ?? ""} | {errorProcessed ?? ""} | {errorSeq ?? ""}".Trim(' ', '|');
                    }
                    else
                    {
                        dto.RdyCount = rdy;
                        dto.TxtCount = txt;
                        dto.Tmp1Count = tmp1; // Kasa
                        dto.Tmp2Count = tmp2; // Inbox
                        dto.DisCount = dis; // Inbox
                        dto.ProCount = pro; // Processed
                        dto.SeqCount = seq; // Seq
                        dto.TotalCount = rdy + txt + tmp1 + tmp2 + dis + pro + seq;
                        dto.Status = dto.TotalCount > 0 ? "dirty" : "clean";
                    }

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

        // ===========================================================
        // 2) TEK PC KONTROL
        // ===========================================================
        [HttpGet("check/{deviceId}")]
        public async Task<IActionResult> CheckSingle(string deviceId)
        {
            var device = await _db.StoreDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId);

            if (device == null)
                return NotFound(new { error = "Device not found" });

            var dto = new InboxStatusDto
            {
                DeviceId = device.DeviceId,
                StoreCode = device.StoreCode,
                StoreName = device.StoreName,
                IpAddress = device.CalculatedIpAddress,
                DeviceType = device.DeviceType
            };

            var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000);
            dto.IsOnline = isOnline;

            if (!isOnline)
            {
                dto.Status = "offline";
                return Ok(dto);
            }

            var uncPath = GetUncPath(device.CalculatedIpAddress);
            var (rdy, txt, tmp2, dis, error) = await GetFileCountsAsync(uncPath, 10);

            var kasaPath = GetKasaPath(device.CalculatedIpAddress);
            var (tmp1, errorKasa) = await GetKasaCountsAsync(kasaPath, 10);

            var proPath = GetProcessedPath(device.CalculatedIpAddress);
            var (pro, errorPro) = await GetProcessedCountsAsync(proPath, 10);

            var seqPath = GetSeqPath(device.CalculatedIpAddress);
            var (seq, errorSeq) = await GetSeqCountsAsync(seqPath, 10);

            if (error != null || errorKasa != null || errorPro != null || errorSeq != null)
            {
                dto.Status = "error";
                dto.ErrorMessage = $"{error ?? ""} | {errorKasa ?? ""} | {errorPro ?? ""} | {errorSeq ?? ""}".Trim(' ', '|');
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

            return Ok(dto);
        }

        // ===========================================================
        // 3) TEK PC TEMİZLE
        // ===========================================================
        [HttpPost("clean/{deviceId}")]
        public async Task<IActionResult> CleanSingle(string deviceId)
        {
            var device = await _db.StoreDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId);

            if (device == null)
                return NotFound(new { error = "Device not found" });

            var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000);
            if (!isOnline)
                return BadRequest(new { error = "Device is offline" });

            // 1) Ready Folder (.rdy, .txt, .tmp, .dis)
            var uncPath = GetUncPath(device.CalculatedIpAddress);
            var (del1, err1) = await DeleteFilesAsync(uncPath, new[] { "*.rdy", "*.txt", "*.tmp", "*.dis" }, 10);

            // 2) Kasa Folder (.tmp)
            var kasaPath = GetKasaPath(device.CalculatedIpAddress);
            var (del2, err2) = await DeleteFilesAsync(kasaPath, new[] { "*.tmp" }, 10);

            // 3) Processed Folder (.rdy, .txt)
            var proPath = GetProcessedPath(device.CalculatedIpAddress);
            var (del3, err3) = await DeleteFilesAsync(proPath, new[] { "*.rdy", "*.txt" }, 60);

            // 4) Seq Folder (.rdy, .txt, .tmp)
            var seqPath = GetSeqPath(device.CalculatedIpAddress);
            var (del4, err4) = await DeleteFilesAsync(seqPath, new[] { "*.rdy", "*.txt", "*.tmp" }, 60);

            if (err1 != null && err2 != null && err3 != null && err4 != null)
                return BadRequest(new { error = $"{err1} | {err2} | {err3} | {err4}" });

            var totalDeleted = del1 + del2 + del3 + del4;
            _logger.LogInformation("Inbox cleaned: {DeviceId} ({Ip}) - {Count} files", device.DeviceId, device.CalculatedIpAddress, totalDeleted);
            return Ok(new { success = true, deleted = totalDeleted, message = $"{device.StoreName} temizlendi ({totalDeleted} dosya)" });
        }

        // ===========================================================
        // 4) TÜM ONLINE PC'LERİ TEMİZLE
        // ===========================================================
        [HttpPost("clean-all")]
        public async Task<IActionResult> CleanAll([FromServices] IInboxCleanupService cleanupService)
        {
            try
            {
                var (successCount, totalCount, results) = await cleanupService.CleanAllAsync();
                return Ok(new { results, successCount, totalCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clean-all islemi sirasinda hata olustu");
                return StatusCode(500, new { error = "Toplu temizlik sirasinda hata olustu.", detail = ex.Message });
            }
        }

        private async Task<(int deleted, string? error)> DeleteFilesAsync(string uncPath, string[] patterns, int timeoutSec = 30)
        {
            try
            {
                var task = Task.Run(() =>
                {
                    if (!Directory.Exists(uncPath))
                        return (0, (string?)"Klasör bulunamadı");

                    int count = 0;
                    foreach (var pattern in patterns)
                    {
                        foreach (var f in Directory.GetFiles(uncPath, pattern))
                        {
                            try { System.IO.File.Delete(f); count++; } catch { }
                        }
                    }
                    return (count, (string?)null);
                });

                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSec))) == task)
                    return await task;

                return (0, "Zaman aşımı");
            }
            catch (Exception ex)
            {
                // Klasör yoksa hata dönme (processed olmayabilir)
                if (ex is DirectoryNotFoundException) return (0, null);
                return (0, ex.Message.Length > 150 ? ex.Message[..150] : ex.Message);
            }
        }
    }
}
