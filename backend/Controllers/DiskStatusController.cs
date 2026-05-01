using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace Orchestra.Backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/disk-status")]
    public class DiskStatusController : ControllerBase
    {
        private readonly OrchestraDbContext _db;
        private readonly ILogger<DiskStatusController> _logger;

        public DiskStatusController(OrchestraDbContext db, ILogger<DiskStatusController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // Win32 API for getting disk space on UNC paths
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetDiskFreeSpaceEx(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailableToCaller,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        // DTO
        public class DiskStatusDto
        {
            public string DeviceId { get; set; } = "";
            public int StoreCode { get; set; }
            public string StoreName { get; set; } = "";
            public string DeviceName { get; set; } = "";
            public string IpAddress { get; set; } = "";
            public bool IsOnline { get; set; }
            public string Status { get; set; } = "unknown"; // online, offline, error

            // C Drive
            public double? DiskCTotalGB { get; set; }
            public double? DiskCUsedGB { get; set; }
            public double? DiskCFreeGB { get; set; }
            public int? DiskCPercent { get; set; }

            // D Drive
            public double? DiskDTotalGB { get; set; }
            public double? DiskDUsedGB { get; set; }
            public double? DiskDFreeGB { get; set; }
            public int? DiskDPercent { get; set; }

            public string? ErrorMessage { get; set; }
        }

        /// <summary>
        /// Tüm mağaza PC'lerinin disk durumunu uzaktan kontrol et
        /// POST: /api/disk-status/check-all
        /// </summary>
        [HttpPost("check-all")]
        public async Task<IActionResult> CheckAll()
        {
            var pcDevices = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici")
                .ToListAsync();

            _logger.LogInformation("Disk status check starting for {Count} devices", pcDevices.Count);

            var results = new ConcurrentBag<DiskStatusDto>();
            using var sem = new SemaphoreSlim(20);

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync();
                try
                {
                    var dto = new DiskStatusDto
                    {
                        DeviceId = device.DeviceId,
                        StoreCode = device.StoreCode,
                        StoreName = device.StoreName,
                        DeviceName = device.DeviceName,
                        IpAddress = device.CalculatedIpAddress
                    };

                    // Online kontrolü (SQL -> SMB -> Ping)
                    var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000);
                    dto.IsOnline = isOnline;

                    if (!isOnline)
                    {
                        dto.Status = "offline";
                        results.Add(dto);
                        return;
                    }

                    // C: Drive
                    var (cTotal, cFree, cError) = await GetDiskSpaceAsync($@"\\{device.CalculatedIpAddress}\C$");
                    if (cError == null && cTotal > 0)
                    {
                        var cUsed = cTotal - cFree;
                        dto.DiskCTotalGB = Math.Round(cTotal / (1024.0 * 1024 * 1024), 1);
                        dto.DiskCFreeGB = Math.Round(cFree / (1024.0 * 1024 * 1024), 1);
                        dto.DiskCUsedGB = Math.Round(cUsed / (1024.0 * 1024 * 1024), 1);
                        dto.DiskCPercent = cTotal > 0 ? (int)Math.Round((double)cUsed / cTotal * 100) : 0;
                    }

                    // D: Drive
                    var (dTotal, dFree, dError) = await GetDiskSpaceAsync($@"\\{device.CalculatedIpAddress}\D$");
                    if (dError == null && dTotal > 0)
                    {
                        var dUsed = dTotal - dFree;
                        dto.DiskDTotalGB = Math.Round(dTotal / (1024.0 * 1024 * 1024), 1);
                        dto.DiskDFreeGB = Math.Round(dFree / (1024.0 * 1024 * 1024), 1);
                        dto.DiskDUsedGB = Math.Round(dUsed / (1024.0 * 1024 * 1024), 1);
                        dto.DiskDPercent = dTotal > 0 ? (int)Math.Round((double)dUsed / dTotal * 100) : 0;
                    }

                    // Status belirleme
                    if (cError != null && dError != null)
                    {
                        dto.Status = "error";
                        dto.ErrorMessage = cError;
                    }
                    else
                    {
                        dto.Status = "online";
                    }

                    results.Add(dto);
                }
                catch (Exception ex)
                {
                    results.Add(new DiskStatusDto
                    {
                        DeviceId = device.DeviceId,
                        StoreCode = device.StoreCode,
                        StoreName = device.StoreName,
                        DeviceName = device.DeviceName,
                        IpAddress = device.CalculatedIpAddress,
                        Status = "error",
                        ErrorMessage = ex.Message.Length > 120 ? ex.Message[..120] : ex.Message
                    });
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);

            var ordered = results.OrderBy(r => r.StoreCode).ThenBy(r => r.DeviceName).ToList();
            _logger.LogInformation("Disk status check done: {Online} online, {Offline} offline, {Error} error",
                ordered.Count(r => r.Status == "online"),
                ordered.Count(r => r.Status == "offline"),
                ordered.Count(r => r.Status == "error"));

            return Ok(ordered);
        }

        /// <summary>
        /// Tüm kasaların disk durumunu uzaktan kontrol et (sadece C:)
        /// POST: /api/disk-status/check-all-kasa
        /// </summary>
        [HttpPost("check-all-kasa")]
        public async Task<IActionResult> CheckAllKasa()
        {
            var kasaDevices = await _db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "Kasa-1" || d.DeviceType == "Kasa-2" || d.DeviceType == "Kasa-3")
                .ToListAsync();

            _logger.LogInformation("Kasa disk status check starting for {Count} devices", kasaDevices.Count);

            var results = new ConcurrentBag<DiskStatusDto>();
            using var sem = new SemaphoreSlim(20);

            var tasks = kasaDevices.Select(async device =>
            {
                await sem.WaitAsync();
                try
                {
                    var dto = new DiskStatusDto
                    {
                        DeviceId = device.DeviceId,
                        StoreCode = device.StoreCode,
                        StoreName = device.StoreName,
                        DeviceName = device.DeviceName,
                        IpAddress = device.CalculatedIpAddress
                    };

                    var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000);
                    dto.IsOnline = isOnline;

                    if (!isOnline)
                    {
                        dto.Status = "offline";
                        results.Add(dto);
                        return;
                    }

                    var (cTotal, cFree, cError) = await GetDiskSpaceAsync($@"\\{device.CalculatedIpAddress}\C$");
                    if (cError == null && cTotal > 0)
                    {
                        var cUsed = cTotal - cFree;
                        dto.DiskCTotalGB = Math.Round(cTotal / (1024.0 * 1024 * 1024), 1);
                        dto.DiskCFreeGB = Math.Round(cFree / (1024.0 * 1024 * 1024), 1);
                        dto.DiskCUsedGB = Math.Round(cUsed / (1024.0 * 1024 * 1024), 1);
                        dto.DiskCPercent = cTotal > 0 ? (int)Math.Round((double)cUsed / cTotal * 100) : 0;
                    }

                    dto.Status = cError != null ? "error" : "online";
                    if (cError != null) dto.ErrorMessage = cError;

                    results.Add(dto);
                }
                catch (Exception ex)
                {
                    results.Add(new DiskStatusDto
                    {
                        DeviceId = device.DeviceId,
                        StoreCode = device.StoreCode,
                        StoreName = device.StoreName,
                        DeviceName = device.DeviceName,
                        IpAddress = device.CalculatedIpAddress,
                        Status = "error",
                        ErrorMessage = ex.Message.Length > 120 ? ex.Message[..120] : ex.Message
                    });
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);

            var ordered = results.OrderBy(r => r.StoreCode).ThenBy(r => r.DeviceName).ToList();
            return Ok(ordered);
        }

        /// <summary>
        /// Tek cihazın disk durumunu kontrol et
        /// GET: /api/disk-status/check/{deviceId}
        /// </summary>
        [HttpGet("check/{deviceId}")]
        public async Task<IActionResult> CheckSingle(string deviceId)
        {
            var device = await _db.StoreDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId);

            if (device == null)
                return NotFound(new { error = "Device not found" });

            var dto = new DiskStatusDto
            {
                DeviceId = device.DeviceId,
                StoreCode = device.StoreCode,
                StoreName = device.StoreName,
                DeviceName = device.DeviceName,
                IpAddress = device.CalculatedIpAddress
            };

            var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000);
            dto.IsOnline = isOnline;

            if (!isOnline)
            {
                dto.Status = "offline";
                return Ok(dto);
            }

            var (cTotal, cFree, cError) = await GetDiskSpaceAsync($@"\\{device.CalculatedIpAddress}\C$");
            if (cError == null && cTotal > 0)
            {
                var cUsed = cTotal - cFree;
                dto.DiskCTotalGB = Math.Round(cTotal / (1024.0 * 1024 * 1024), 1);
                dto.DiskCFreeGB = Math.Round(cFree / (1024.0 * 1024 * 1024), 1);
                dto.DiskCUsedGB = Math.Round(cUsed / (1024.0 * 1024 * 1024), 1);
                dto.DiskCPercent = cTotal > 0 ? (int)Math.Round((double)cUsed / cTotal * 100) : 0;
            }

            var (dTotal, dFree, dError) = await GetDiskSpaceAsync($@"\\{device.CalculatedIpAddress}\D$");
            if (dError == null && dTotal > 0)
            {
                var dUsed = dTotal - dFree;
                dto.DiskDTotalGB = Math.Round(dTotal / (1024.0 * 1024 * 1024), 1);
                dto.DiskDFreeGB = Math.Round(dFree / (1024.0 * 1024 * 1024), 1);
                dto.DiskDUsedGB = Math.Round(dUsed / (1024.0 * 1024 * 1024), 1);
                dto.DiskDPercent = dTotal > 0 ? (int)Math.Round((double)dUsed / dTotal * 100) : 0;
            }

            dto.Status = (cError != null && dError != null) ? "error" : "online";
            if (cError != null && dError != null) dto.ErrorMessage = cError;

            return Ok(dto);
        }

        // ===================== HELPER METHODS =====================

        private async Task<bool> IsDeviceOnlineAsync(string ip, int timeoutMs = 2000)
        {
            // 1. SQL Port (1433)
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, 1433);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask && client.Connected)
                    return true;
            }
            catch { }

            // 2. SMB Port (445)
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, 445);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask && client.Connected)
                    return true;
            }
            catch { }

            // 3. Ping
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(ip, timeoutMs);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    return true;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// UNC path üzerinden GetDiskFreeSpaceEx Win32 API ile disk alanı bilgisi al
        /// </summary>
        private async Task<(ulong totalBytes, ulong freeBytes, string? error)> GetDiskSpaceAsync(string uncPath, int timeoutSec = 15)
        {
            try
            {
                var task = Task.Run(() =>
                {
                    if (GetDiskFreeSpaceEx(uncPath, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes))
                    {
                        return (totalBytes, totalFreeBytes, (string?)null);
                    }
                    else
                    {
                        var errorCode = Marshal.GetLastWin32Error();
                        return ((ulong)0, (ulong)0, (string?)$"GetDiskFreeSpaceEx failed: error {errorCode}");
                    }
                });

                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSec))) == task)
                    return await task;

                return (0, 0, "Zaman aşımı");
            }
            catch (Exception ex)
            {
                return (0, 0, ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
        }
    }
}
