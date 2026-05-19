using Orchestra.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Orchestra.Backend.Services
{
    public interface IStockCleanupService
    {
        Task<StockCleanupResult> CleanStoresWithErrorsAsync(CancellationToken ct = default);
    }

    public class StockCleanupResult
    {
        public int TotalChecked { get; set; }
        public int OnlineCount { get; set; }
        public int OfflineCount { get; set; }
        public int CleanedCount { get; set; }
        public int SkippedCleanCount { get; set; }
        public int ErrorCount { get; set; }
    }

    public class StockCleanupService : IStockCleanupService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StockCleanupService> _logger;

        public StockCleanupService(IServiceScopeFactory scopeFactory, ILogger<StockCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<StockCleanupResult> CleanStoresWithErrorsAsync(CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var remoteSql = scope.ServiceProvider.GetRequiredService<IRemoteSqlService>();
            var fastCheck = scope.ServiceProvider.GetRequiredService<FastSqlReachabilityService>();

            var pcDevices = await db.StoreDevices
                .AsNoTracking()
                .Where(d => d.DeviceType == "PC" || d.DeviceType.ToLower() == "gecici")
                .ToListAsync(ct);

            var result = new StockCleanupResult { TotalChecked = pcDevices.Count };
            var lockObj = new object();
            using var sem = new SemaphoreSlim(20);

            var tasks = pcDevices.Select(async device =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var isOnline = await fastCheck.IsSqlReachableAsync(device.CalculatedIpAddress, 1433, 2000);
                    if (!isOnline)
                    {
                        lock (lockObj) result.OfflineCount++;
                        return;
                    }
                    lock (lockObj) result.OnlineCount++;

                    const string countQuery = "SELECT COUNT(*) AS Plu30 FROM POS_STOCK_TRANSFER WHERE OK > 30";
                    var dt = await remoteSql.ExecuteQueryAsync(device.DbConnectionString, countQuery);

                    int plu30 = 0;
                    if (dt != null && dt.Rows.Count > 0 && dt.Rows[0]["Plu30"] != DBNull.Value)
                        plu30 = Convert.ToInt32(dt.Rows[0]["Plu30"]);

                    if (plu30 <= 0)
                    {
                        lock (lockObj) result.SkippedCleanCount++;
                        return;
                    }

                    await remoteSql.ExecuteQueryAsync(device.DbConnectionString, "TRUNCATE TABLE POS_STOCK_TRANSFER");
                    _logger.LogInformation("StockCleanup truncated {Store} ({Ip}) - Plu30={Count}",
                        device.StoreName, device.CalculatedIpAddress, plu30);
                    lock (lockObj) result.CleanedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "StockCleanup device error: {DeviceId} ({Store})", device.DeviceId, device.StoreName);
                    lock (lockObj) result.ErrorCount++;
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);

            _logger.LogInformation(
                "StockCleanup done: total={Total} online={Online} offline={Offline} cleaned={Cleaned} skipped={Skip} error={Err}",
                result.TotalChecked, result.OnlineCount, result.OfflineCount,
                result.CleanedCount, result.SkippedCleanCount, result.ErrorCount);

            return result;
        }
    }
}
