using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;

namespace Orchestra.Backend.Services;

/// <summary>
/// Ayda 1 (ve manuel tetikleme ile) tum bilinen Windows cihazlarinin BIOS seri numarasini
/// remote WMI uzerinden cekip Devices.SerialNumber kolonuna yazar. Agent'a bagimli degildir —
/// online ve IP'si olan her Device satiri icin PowerShell Get-CimInstance ile sorgulanir.
/// Backend hesabi hedef makinede admin yetkili olmalidir (sc.exe \\IP altyapisi ile ayni varsayim).
/// </summary>
public class SerialNumberSyncService : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromDays(30);
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    private const int MaxParallel = 10;
    private const int PerHostTimeoutSeconds = 15;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SerialNumberSyncService> _logger;
    private static DateTime _lastRunUtc = DateTime.MinValue;
    private static readonly SemaphoreSlim _runLock = new(1, 1);

    public SerialNumberSyncService(IServiceProvider serviceProvider, ILogger<SerialNumberSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public static DateTime LastRunUtc => _lastRunUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Acilista 2 dakika bekle — diger worker'lar ayaga kalksin
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
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
                _logger.LogError(ex, "SerialNumberSync tick hatasi");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task<SerialSyncResult> RunSyncAsync(CancellationToken ct)
    {
        if (!await _runLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("SerialNumberSync zaten calisiyor, yeni tetikleme atlandi.");
            return new SerialSyncResult { Skipped = true };
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();

            // Hem agent'li PC'ler (Devices) hem kasalar (StoreDevices) hedef.
            // IP'ye gore birlestir — ayni IP iki tabloda da olabilir, tek wmic ile ikisini de doldur.
            var deviceTargets = await db.Devices
                .Where(d => d.Online && !string.IsNullOrEmpty(d.IpAddress))
                .Select(d => new { d.IpAddress, d.Hostname })
                .ToListAsync(ct);

            var storeDeviceTargets = await db.StoreDevices
                .Where(sd => !sd.IsTemporarilyClosed && !string.IsNullOrEmpty(sd.CalculatedIpAddress))
                .Select(sd => new { IpAddress = sd.CalculatedIpAddress, Hostname = sd.DeviceName })
                .ToListAsync(ct);

            var uniqueIps = deviceTargets.Select(t => t.IpAddress!)
                .Concat(storeDeviceTargets.Select(t => t.IpAddress))
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("SerialNumberSync basliyor: {Count} benzersiz IP ({DevCount} agent, {StoreCount} StoreDevice)",
                uniqueIps.Count, deviceTargets.Count, storeDeviceTargets.Count);

            var result = new SerialSyncResult { Total = uniqueIps.Count };
            using var sem = new SemaphoreSlim(MaxParallel);
            var tasks = uniqueIps.Select(async ip =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var serial = await QueryRemoteSerialAsync(ip, ct);
                    if (string.IsNullOrWhiteSpace(serial))
                    {
                        Interlocked.Increment(ref result._failed);
                        return;
                    }

                    using var innerScope = _serviceProvider.CreateScope();
                    var innerDb = innerScope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
                    var changed = false;

                    // Devices tablosu (IP ile match)
                    var devices = await innerDb.Devices.Where(d => d.IpAddress == ip).ToListAsync(ct);
                    foreach (var d in devices)
                    {
                        if (!string.Equals(d.SerialNumber, serial, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Seri guncellendi (Device): {Host} ({Ip}) {Old} -> {New}",
                                d.Hostname, ip, d.SerialNumber ?? "(bos)", serial);
                            d.SerialNumber = serial;
                            changed = true;
                        }
                    }

                    // StoreDevices tablosu (CalculatedIpAddress ile match)
                    var stores = await innerDb.StoreDevices.Where(sd => sd.CalculatedIpAddress == ip).ToListAsync(ct);
                    foreach (var sd in stores)
                    {
                        if (!string.Equals(sd.SerialNumber, serial, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Seri guncellendi (StoreDevice): {Host} ({Ip}) {Old} -> {New}",
                                sd.DeviceName, ip, sd.SerialNumber ?? "(bos)", serial);
                            sd.SerialNumber = serial;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        await innerDb.SaveChangesAsync(ct);
                        Interlocked.Increment(ref result._updated);
                    }
                    else
                    {
                        Interlocked.Increment(ref result._unchanged);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Seri sorgu hatasi: {Ip}", ip);
                    Interlocked.Increment(ref result._failed);
                }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);

            _lastRunUtc = DateTime.UtcNow;
            result.CompletedAtUtc = _lastRunUtc;
            _logger.LogInformation("SerialNumberSync bitti: total={Total} updated={Updated} unchanged={Unchanged} failed={Failed}",
                result.Total, result.Updated, result.Unchanged, result.Failed);
            return result;
        }
        finally
        {
            _runLock.Release();
        }
    }

    /// <summary>
    /// Tek bir IP icin BIOS seri numarasini remote wmic ile cekip dondurur. Endpoint'lerden
    /// anlik fallback olarak da cagrilir (kasalar Devices tablosunda olmayabilir).
    /// </summary>
    public static Task<string?> QuerySerialAsync(string ipAddress, CancellationToken ct)
        => QueryRemoteSerialAsync(ipAddress, ct);

    private static async Task<string?> QueryRemoteSerialAsync(string ipAddress, CancellationToken ct)
    {
        // wmic /node:IP — RPC/DCOM (port 135) uzerinden Win32_BIOS.SerialNumber.
        // PowerShell Get-CimInstance WSMan kullanir ve sahada port 5985 kapali oldugundan
        // basarisiz olur; sc.exe \\IP ile ayni RPC altyapisini kullanan wmic surekli calisir.
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c wmic /node:{ipAddress} bios get SerialNumber /value",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        using var killTimer = new CancellationTokenSource(TimeSpan.FromSeconds(PerHostTimeoutSeconds + 5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, killTimer.Token);

        try
        {
            var outTask = process.StandardOutput.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            var raw = (await outTask).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // wmic ciktisi: bir cok bos satir + "SerialNumber=XXXX"
            string? value = null;
            foreach (var line in raw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("SerialNumber=", StringComparison.OrdinalIgnoreCase))
                {
                    value = trimmed.Substring("SerialNumber=".Length).Trim();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(value)) return null;

            // Placeholder degerleri filtrele
            if (value.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Default string", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("System Serial Number", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return value;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            return null;
        }
    }

    public class SerialSyncResult
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
