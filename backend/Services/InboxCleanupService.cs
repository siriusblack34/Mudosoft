using Orchestra.Backend.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace Orchestra.Backend.Services
{
    public class CleanResultItem
    {
        public string DeviceId { get; set; } = "";
        public string StoreName { get; set; } = "";
        public bool Success { get; set; }
        public string Reason { get; set; } = "";
    }

    public interface IInboxCleanupService
    {
        Task<(int successCount, int totalCount, List<CleanResultItem> results)> CleanAllAsync(
            IProgress<CleanResultItem>? progress = null,
            CancellationToken ct = default);
    }

    public class InboxCleanupService : IInboxCleanupService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InboxCleanupService> _logger;

        private const string READY_FOLDER     = @"GeniusOpen\Inbox\000\Ready";
        private const string KASA_FOLDER      = @"GeniusOpen\Kasa";
        private const string PROCESSED_FOLDER = @"GeniusOpen\Inbox\000\Ready\processed";
        private const string SEQ_FOLDER       = @"GeniusOpen\Inbox\000\Seq";

        public InboxCleanupService(IServiceScopeFactory scopeFactory, ILogger<InboxCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        private static string GetUncPath(string ip)       => $@"\\{ip}\C$\{READY_FOLDER}";
        private static string GetKasaPath(string ip)      => $@"\\{ip}\C$\{KASA_FOLDER}";
        private static string GetProcessedPath(string ip) => $@"\\{ip}\C$\{PROCESSED_FOLDER}";
        private static string GetSeqPath(string ip)       => $@"\\{ip}\C$\{SEQ_FOLDER}";

        public async Task<(int successCount, int totalCount, List<CleanResultItem> results)> CleanAllAsync(
            IProgress<CleanResultItem>? progress = null,
            CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            var pcDevices = await db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici")
                .ToListAsync(ct);

            var results = new ConcurrentBag<CleanResultItem>();
            using var sem = new SemaphoreSlim(10);

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    CleanResultItem item;
                    var (isOnline, smbOpen) = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000, ct);

                    if (!isOnline)
                    {
                        item = new CleanResultItem { DeviceId = device.DeviceId, StoreName = device.StoreName, Success = false, Reason = "offline" };
                    }
                    else if (!smbOpen)
                    {
                        item = new CleanResultItem { DeviceId = device.DeviceId, StoreName = device.StoreName, Success = false, Reason = "SMB (port 445) kapalı" };
                    }
                    else
                    {
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
                        {
                            item = new CleanResultItem { DeviceId = device.DeviceId, StoreName = device.StoreName, Success = false, Reason = anyError };
                        }
                        else
                        {
                            var parts = new List<string>();
                            if (directDeleted > 0) parts.Add($"{directDeleted} dosya");
                            if (renamedCount > 0) parts.Add($"{renamedCount} klasör yenilendi");
                            var reason = parts.Count > 0 ? string.Join(", ", parts) : "zaten temiz";
                            item = new CleanResultItem { DeviceId = device.DeviceId, StoreName = device.StoreName, Success = true, Reason = reason };
                        }

                        _logger.LogInformation("CleanAll: {Store} ({Ip}) — {Count} direkt, {Renamed} klasör yenilendi",
                            device.StoreName, ip, directDeleted, renamedCount);
                    }

                    results.Add(item);
                    progress?.Report(item);
                }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);

            var resultList = results.ToList();
            var successCount = resultList.Count(r => r.Success);
            return (successCount, resultList.Count, resultList);
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
                        try { File.Delete(file); deleted++; } catch { }
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
                            try { File.Delete(f); deleted++; } catch { }
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

        private static async Task<(bool online, bool smbOpen)> IsDeviceOnlineAsync(
            string ip, int timeoutMs, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            var token = cts.Token;

            var sqlTask  = TryTcpConnectAsync(ip, 1433, token);
            var smbTask  = TryTcpConnectAsync(ip, 445,  token);
            var pingTask = PingAsync(ip, Math.Max(500, timeoutMs / 3));

            try { await Task.WhenAll(sqlTask, smbTask, pingTask); } catch { }

            var smbOpen = smbTask.IsCompletedSuccessfully && smbTask.Result;
            var online  = smbOpen
                || (sqlTask.IsCompletedSuccessfully && sqlTask.Result)
                || (pingTask.IsCompletedSuccessfully && pingTask.Result);

            return (online, smbOpen);
        }

        private static async Task<bool> TryTcpConnectAsync(string ip, int port, CancellationToken ct)
        {
            try { using var c = new TcpClient(); await c.ConnectAsync(ip, port, ct); return c.Connected; }
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

    }
}
