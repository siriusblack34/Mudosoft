using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using System.Net.Sockets;

namespace MudoSoft.Backend.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/inbox-cleanup")]
    public class InboxCleanupController : ControllerBase
    {
        private readonly MudoSoftDbContext _db;
        private readonly ILogger<InboxCleanupController> _logger;

        private const string READY_FOLDER = @"GeniusOpen\Inbox\000\Ready";
        private const string KASA_FOLDER = @"GeniusOpen\Kasa";

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
            public bool IsOnline { get; set; }
            public int RdyCount { get; set; }
            public int TxtCount { get; set; }
            public int Tmp1Count { get; set; } // Kasa
            public int Tmp2Count { get; set; } // Ready
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
        private async Task<(int tmp, string? error)> GetKasaCountsAsync(string uncPath, int timeoutSec = 20)
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

        /// <summary>
        /// UNC path ile dosyaları sil — timeout korumalı
        /// </summary>
        private async Task<(int deleted, string? error)> DeleteFilesAsync(string uncPath, string[] extensions, int timeoutSec = 30)
        {
            try
            {
                var task = Task.Run(() =>
                {
                    if (!Directory.Exists(uncPath))
                        return (0, (string?)"Klasör bulunamadı");

                    int count = 0;
                    foreach (var ext in extensions)
                    {
                        foreach (var f in Directory.GetFiles(uncPath, ext))
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
                return (0, ex.Message.Length > 150 ? ex.Message[..150] : ex.Message);
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
                .Where(d => d.DeviceType == "PC")
                .ToListAsync();

            _logger.LogInformation("Inbox check-all starting for {Count} PCs", pcDevices.Count);

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
                        IpAddress = device.CalculatedIpAddress
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

                    // 1. Inbox (Ready) sayımı (rdy, txt, tmp2)
                    var uncPathInbox = GetUncPath(device.CalculatedIpAddress);
                    var (rdy, txt, tmp2, errorInbox) = await GetInboxCountsAsync(uncPathInbox, 10);

                    // 2. Kasa sayımı (tmp1)
                    var uncPathKasa = GetKasaPath(device.CalculatedIpAddress);
                    var (tmp1, errorKasa) = await GetKasaCountsAsync(uncPathKasa, 10);

                    if (errorInbox != null && errorKasa != null)
                    {
                        dto.Status = "error";
                        dto.ErrorMessage = $"{errorInbox} | {errorKasa}";
                    }
                    else
                    {
                        dto.RdyCount = rdy;
                        dto.TxtCount = txt;
                        dto.Tmp1Count = tmp1; // Kasa
                        dto.Tmp2Count = tmp2; // Inbox
                        dto.TotalCount = rdy + txt + tmp1 + tmp2;
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
            return Ok(ordered);
        }

        // ===========================================================
        // 2) TEK PC KONTROL
        // ===========================================================
        [HttpPost("check/{deviceId}")]
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
                IpAddress = device.CalculatedIpAddress
            };

            var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000);
            dto.IsOnline = isOnline;

            if (!isOnline)
            {
                dto.Status = "offline";
                return Ok(dto);
            }

            // 1. Inbox
            var uncPathInbox = GetUncPath(device.CalculatedIpAddress);
            var (rdy, txt, tmp2, errorInbox) = await GetInboxCountsAsync(uncPathInbox, 10);
            
            // 2. Kasa
            var uncPathKasa = GetKasaPath(device.CalculatedIpAddress);
            var (tmp1, errorKasa) = await GetKasaCountsAsync(uncPathKasa, 10);

            if (errorInbox != null && errorKasa != null)
            {
                dto.Status = "error";
                dto.ErrorMessage = $"{errorInbox} | {errorKasa}";
            }
            else
            {
                dto.RdyCount = rdy;
                dto.TxtCount = txt;
                dto.Tmp1Count = tmp1;
                dto.Tmp2Count = tmp2;
                dto.TotalCount = rdy + txt + tmp1 + tmp2;
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

            // 1. Inbox Temizle: rdy, txt, tmp
            var uncPathInbox = GetUncPath(device.CalculatedIpAddress);
            var (delInbox, errInbox) = await DeleteFilesAsync(uncPathInbox, new[] { "*.rdy", "*.txt", "*.tmp" }, 10);

            // 2. Kasa Temizle: tmp
            var uncPathKasa = GetKasaPath(device.CalculatedIpAddress);
            var (delKasa, errKasa) = await DeleteFilesAsync(uncPathKasa, new[] { "*.tmp" }, 10);

            int totalDeleted = delInbox + delKasa;

            _logger.LogInformation("Inbox cleaned: {DeviceId} ({Ip}) - {Count} files", device.DeviceId, device.CalculatedIpAddress, totalDeleted);
            return Ok(new { success = true, deleted = totalDeleted, message = $"{device.StoreName} temizlendi ({totalDeleted} dosya)" });
        }

        // ===========================================================
        // 4) TÜM ONLINE PC'LERİ TEMİZLE
        // ===========================================================
        [HttpPost("clean-all")]
        public async Task<IActionResult> CleanAll()
        {
            var pcDevices = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC")
                .ToListAsync();

            var results = new List<object>();
            using var sem = new SemaphoreSlim(20);

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync();
                try
                {
                    var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000);
                    if (!isOnline)
                    {
                        lock (results) results.Add(new { device.DeviceId, device.StoreName, success = false, reason = "offline" });
                        return;
                    }

                    // Inbox Clean
                    var uncPathInbox = GetUncPath(device.CalculatedIpAddress);
                    var (delInbox, _) = await DeleteFilesAsync(uncPathInbox, new[] { "*.rdy", "*.txt", "*.tmp" }, 10);

                    // Kasa Clean
                    var uncPathKasa = GetKasaPath(device.CalculatedIpAddress);
                    var (delKasa, _) = await DeleteFilesAsync(uncPathKasa, new[] { "*.tmp" }, 10);

                    int total = delInbox + delKasa;
                    lock (results) results.Add(new { device.DeviceId, device.StoreName, success = true, reason = $"{total} dosya silindi" });
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);

            var successCount = results.Cast<dynamic>().Count(r => r.success);
            return Ok(new { results, successCount, totalCount = results.Count });
        }
    }
}
