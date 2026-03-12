using MudoSoft.Backend.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading; // SemaphoreSlim
using System.Threading.Tasks; // Task
using System.Linq; // Select

namespace MudoSoft.Backend.Services
{
    public interface IInboxCleanupService
    {
        Task<(int successCount, int totalCount, List<object> results)> CleanAllAsync();
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

        public async Task<(int successCount, int totalCount, List<object> results)> CleanAllAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MudoSoftDbContext>();
            
            var pcDevices = await db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici")
                .ToListAsync();

            var results = new ConcurrentBag<object>();
            using var sem = new SemaphoreSlim(20);

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync();
                try
                {
                    var isOnline = await IsDeviceOnlineAsync(device.CalculatedIpAddress, 2000);
                    if (!isOnline)
                    {
                        results.Add(new { device.DeviceId, device.StoreName, success = false, reason = "offline" });
                        return;
                    }

                    // 1) Ready
                    var uncPath = GetUncPath(device.CalculatedIpAddress);
                    var (del1, err1) = await DeleteFilesAsync(uncPath, new[] { "*.rdy", "*.txt", "*.tmp", "*.dis" }, 10);

                    // 2) Kasa
                    var kasaPath = GetKasaPath(device.CalculatedIpAddress);
                    var (del2, err2) = await DeleteFilesAsync(kasaPath, new[] { "*.tmp" }, 10);

                    // 3) Processed
                    var proPath = GetProcessedPath(device.CalculatedIpAddress);
                    var (del3, err3) = await DeleteFilesAsync(proPath, new[] { "*.rdy", "*.txt" }, 60);

                    // 4) Seq
                    var seqPath = GetSeqPath(device.CalculatedIpAddress);
                    var (del4, err4) = await DeleteFilesAsync(seqPath, new[] { "*.rdy", "*.txt", "*.tmp" }, 60);

                    var totalDeleted = del1 + del2 + del3 + del4;
                    var anyError = err1 ?? err2 ?? err3 ?? err4;

                    if (totalDeleted == 0 && anyError != null)
                        results.Add(new { device.DeviceId, device.StoreName, success = false, reason = anyError });
                    else
                        results.Add(new { device.DeviceId, device.StoreName, success = true, reason = $"{totalDeleted} dosya silindi" });
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);

            var resultList = results.ToList();
            var successCount = resultList.Cast<dynamic>().Count(r => r.success);
            
            return (successCount, resultList.Count, resultList);
        }

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
                if (ex is DirectoryNotFoundException) return (0, null);
                return (0, ex.Message.Length > 150 ? ex.Message[..150] : ex.Message);
            }
        }
    }
}
