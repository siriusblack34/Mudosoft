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

        private const string READY_FOLDER = @"GeniusOpen\Inbox\000\Ready";
        private const string KASA_FOLDER = @"GeniusOpen\Kasa";
        private const string PROCESSED_FOLDER = @"GeniusOpen\Inbox\000\Ready\processed";
        private const string SEQ_FOLDER = @"GeniusOpen\Inbox\000\Seq";

        public InboxCleanupService(IServiceScopeFactory scopeFactory, ILogger<InboxCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        private string GetUncPath(string ip) => $@"\\{ip}\C$\{READY_FOLDER}";
        private string GetKasaPath(string ip) => $@"\\{ip}\C$\{KASA_FOLDER}";
        private string GetProcessedPath(string ip) => $@"\\{ip}\C$\{PROCESSED_FOLDER}";
        private string GetSeqPath(string ip) => $@"\\{ip}\C$\{SEQ_FOLDER}";

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
                    var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000, ct);
                    if (!isOnline)
                    {
                        item = new CleanResultItem { DeviceId = device.DeviceId, StoreName = device.StoreName, Success = false, Reason = "offline" };
                    }
                    else
                    {
                        var ip = device.CalculatedIpAddress;

                        var t1 = DeleteFilesAsync(GetUncPath(ip), new[] { "*.rdy", "*.txt", "*.tmp", "*.dis" }, 30, ct);
                        var t2 = DeleteFilesAsync(GetKasaPath(ip), new[] { "*.tmp" }, 30, ct);
                        var t3 = DeleteFilesAsync(GetProcessedPath(ip), new[] { "*.rdy", "*.txt" }, 60, ct);
                        var t4 = DeleteFilesAsync(GetSeqPath(ip), new[] { "*.rdy", "*.txt", "*.tmp" }, 60, ct);

                        await Task.WhenAll(t1, t2, t3, t4);

                        var (del1, err1) = await t1;
                        var (del2, err2) = await t2;
                        var (del3, err3) = await t3;
                        var (del4, err4) = await t4;

                        var totalDeleted = del1 + del2 + del3 + del4;
                        var anyError = err1 ?? err2 ?? err3 ?? err4;

                        item = (totalDeleted == 0 && anyError != null)
                            ? new CleanResultItem { DeviceId = device.DeviceId, StoreName = device.StoreName, Success = false, Reason = anyError }
                            : new CleanResultItem { DeviceId = device.DeviceId, StoreName = device.StoreName, Success = true, Reason = $"{totalDeleted} dosya silindi" };
                    }

                    results.Add(item);
                    progress?.Report(item);
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);

            var resultList = results.ToList();
            var successCount = resultList.Count(r => r.Success);

            return (successCount, resultList.Count, resultList);
        }

        private static async Task<bool> IsDeviceOnlineAsync(string ip, int timeoutMs, CancellationToken ct)
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

        private static async Task<(int deleted, string? error)> DeleteFilesAsync(
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
