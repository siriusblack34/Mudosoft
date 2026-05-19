using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;

namespace Orchestra.Backend.Services;

/// <summary>
/// Kasalarin (StoreDevices.DeviceType KK ile baslayan) GENIUS DB'sindeki TRANSACTION_RESULT
/// tablosundan OKC yazici sicil numarasini cekip StoreDevices.PrinterSerialNumber kolonuna yazar.
/// Sicil numarasi cok nadir degisir; ayda 1 sessizce tarama yeterli.
/// </summary>
public class PrinterSerialSyncService : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromDays(30);
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    private const int MaxParallel = 8;
    private const int PerQueryTimeoutSeconds = 15;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PrinterSerialSyncService> _logger;
    private static DateTime _lastRunUtc = DateTime.MinValue;
    private static readonly SemaphoreSlim _runLock = new(1, 1);

    public PrinterSerialSyncService(IServiceProvider serviceProvider, ILogger<PrinterSerialSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public static DateTime LastRunUtc => _lastRunUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - _lastRunUtc >= SyncInterval)
                {
                    await RunSyncAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PrinterSerialSync tick hatasi");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task<PrinterSyncResult> RunSyncAsync(CancellationToken ct)
    {
        if (!await _runLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("PrinterSerialSync zaten calisiyor, yeni tetikleme atlandi.");
            return new PrinterSyncResult { Skipped = true };
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var sql = scope.ServiceProvider.GetRequiredService<IRemoteSqlService>();

            var targets = await db.StoreDevices
                .Where(sd => sd.DeviceType.StartsWith("Kasa")
                    && !sd.IsTemporarilyClosed
                    && sd.DbConnectionString != ""
                    && sd.CalculatedIpAddress != "")
                .Select(sd => new { sd.DeviceId, sd.DeviceName, sd.CalculatedIpAddress, sd.DbConnectionString, sd.PrinterSerialNumber })
                .ToListAsync(ct);

            _logger.LogInformation("PrinterSerialSync basliyor: {Count} kasa hedef", targets.Count);

            var result = new PrinterSyncResult { Total = targets.Count };
            using var sem = new SemaphoreSlim(MaxParallel);
            var tasks = targets.Select(async t =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var serial = await QueryPrinterSerialAsync(sql, t.DbConnectionString, ct);
                    if (string.IsNullOrWhiteSpace(serial))
                    {
                        Interlocked.Increment(ref result._failed);
                        return;
                    }

                    if (!string.Equals(serial, t.PrinterSerialNumber, StringComparison.OrdinalIgnoreCase))
                    {
                        using var innerScope = _serviceProvider.CreateScope();
                        var innerDb = innerScope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
                        var sd = await innerDb.StoreDevices.FirstOrDefaultAsync(x => x.DeviceId == t.DeviceId, ct);
                        if (sd != null)
                        {
                            _logger.LogInformation("Printer sicil guncellendi: {Name} ({Ip}) {Old} -> {New}",
                                t.DeviceName, t.CalculatedIpAddress, t.PrinterSerialNumber ?? "(bos)", serial);
                            sd.PrinterSerialNumber = serial;
                            await innerDb.SaveChangesAsync(ct);
                            Interlocked.Increment(ref result._updated);
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref result._unchanged);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Printer sicil sorgu hatasi: {Name} ({Ip})", t.DeviceName, t.CalculatedIpAddress);
                    Interlocked.Increment(ref result._failed);
                }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);

            _lastRunUtc = DateTime.UtcNow;
            result.CompletedAtUtc = _lastRunUtc;
            _logger.LogInformation("PrinterSerialSync bitti: total={Total} updated={Updated} unchanged={Unchanged} failed={Failed}",
                result.Total, result.Updated, result.Unchanged, result.Failed);
            return result;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private static async Task<string?> QueryPrinterSerialAsync(IRemoteSqlService sql, string connStr, CancellationToken ct)
    {
        // TRANSACTION_RESULT'da OKC sicil no PARAMETER_1 sutununda "YAB" prefix'iyle tutuluyor.
        // En son satira gerek yok — DISTINCT al, format dogru olani sec.
        const string query = "SELECT TOP 1 PARAMETER_1 AS sicil FROM TRANSACTION_RESULT WHERE PARAMETER_1 LIKE 'YAB%' ORDER BY CREATE_DATE DESC";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(PerQueryTimeoutSeconds));

        var dt = await sql.ExecuteQueryAsync(connStr, query);
        if (dt == null || dt.Rows.Count == 0) return null;
        var raw = dt.Rows[0]["sicil"]?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw;
    }

    public class PrinterSyncResult
    {
        internal int _updated;
        internal int _unchanged;
        internal int _failed;
        public int Total { get; set; }
        public int Updated => _updated;
        public int Unchanged => _unchanged;
        public int Failed => _failed;
        public bool Skipped { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }
}
