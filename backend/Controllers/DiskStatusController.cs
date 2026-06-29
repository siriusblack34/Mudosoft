using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Shared.Dtos;
using Orchestra.Shared.Enums;
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
        private readonly CommandQueue _queue;

        public DiskStatusController(OrchestraDbContext db, ILogger<DiskStatusController> logger, CommandQueue queue)
        {
            _db = db;
            _logger = logger;
            _queue = queue;
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

        public class DiskAnalysisResult
        {
            public string DeviceId { get; set; } = "";
            public string DeviceName { get; set; } = "";
            public string StoreName { get; set; } = "";
            public string IpAddress { get; set; } = "";
            public List<FolderSizeItem> Folders { get; set; } = new();
            public string? Error { get; set; }
        }

        public class FolderSizeItem
        {
            public string Name { get; set; } = "";
            public string LocalPath { get; set; } = "";
            public double SizeGB { get; set; }
            public long FileCount { get; set; }
            public bool TimedOut { get; set; }
            public string Category { get; set; } = "other"; // temp, updates, pos, users, logs, system
        }

        /// <summary>
        /// Cihazın C: diskindeki hangi klasörlerin yer kapladığını analiz et (SMB UNC üzerinden)
        /// GET: /api/disk-status/analyze/{deviceId}
        /// </summary>
        [HttpGet("analyze/{deviceId}")]
        public async Task<IActionResult> AnalyzeDisk(string deviceId)
        {
            var device = await _db.StoreDevices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device == null) return NotFound(new { error = "Device not found" });

            var ip = device.CalculatedIpAddress;
            var uncRoot = $@"\\{ip}\C$";

            var result = new DiskAnalysisResult
            {
                DeviceId = device.DeviceId,
                DeviceName = device.DeviceName,
                StoreName = device.StoreName,
                IpAddress = ip
            };

            // Root erişim kontrolü
            try
            {
                var rootCheckTask = Task.Run(() => Directory.Exists(uncRoot));
                if (!await rootCheckTask.WaitAsync(TimeSpan.FromSeconds(5)) || !rootCheckTask.Result)
                {
                    result.Error = "C$ paylaşımına erişilemedi";
                    return Ok(result);
                }
            }
            catch (Exception ex)
            {
                result.Error = $"C$ erişim hatası: {ex.Message[..Math.Min(100, ex.Message.Length)]}";
                return Ok(result);
            }

            // Bilinen doluluk nedenleri — (göreli yol, kategori)
            var targets = new List<(string Rel, string Cat)>
            {
                (@"Windows\Temp",                                        "temp"),
                (@"Windows\SoftwareDistribution\Download",               "updates"),
                (@"Windows\SoftwareDistribution\DataStore",              "updates"),
                (@"Windows\SoftwareDistribution\DeliveryOptimization",   "updates"),
                (@"Windows\Logs",                                        "logs"),
                (@"Windows\System32\winevt\Logs",                        "logs"),
                (@"Windows\Installer",                                   "installer"),
                (@"Windows.old",                                         "system"),
                (@"Windows\Minidump",                                    "dumps"),
                (@"Windows\Prefetch",                                    "cache"),
                (@"Windows\CbsTemp",                                     "temp"),
                (@"ProgramData\Microsoft\Windows\WER\ReportQueue",       "wer"),
                (@"ProgramData\Microsoft\Windows\WER\ReportArchive",     "wer"),
                (@"$Recycle.Bin",                                        "temp"),
                (@"Temp",                                                "temp"),
                (@"TEMP",                                                "temp"),
                (@"GeniusOpen",                                          "pos"),
                (@"Genius",                                              "pos"),
                (@"GeniusData",                                          "pos"),
                (@"inetpub\logs",                                        "logs"),
                (@"Logs",                                                "logs"),
                (@"LOGS",                                                "logs"),
                (@"ProgramData",                                         "system"),
            };

            var folders = new ConcurrentBag<FolderSizeItem>();
            using var globalCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using var sem = new SemaphoreSlim(4);

            // Hedef klasörleri paralel tara
            var targetTasks = targets.Select(async t =>
            {
                var uncPath = Path.Combine(uncRoot, t.Rel);
                try { if (!Directory.Exists(uncPath)) return; } catch { return; }

                await sem.WaitAsync(globalCts.Token);
                try
                {
                    var (bytes, count, timedOut) = await ScanFolderAsync(uncPath, globalCts.Token);
                    folders.Add(new FolderSizeItem
                    {
                        Name = t.Rel,
                        LocalPath = "C:\\" + t.Rel,
                        SizeGB = Math.Round(bytes / (1024.0 * 1024 * 1024), 2),
                        FileCount = count,
                        TimedOut = timedOut,
                        Category = t.Cat
                    });
                }
                catch (OperationCanceledException) { }
                finally { sem.Release(); }
            }).ToList();

            // Kullanıcı profillerinden hedefli alt klasörler
            Task usersTask = Task.CompletedTask;
            try
            {
                var usersUncPath = Path.Combine(uncRoot, "Users");
                if (Directory.Exists(usersUncPath))
                {
                    var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { "Public", "Default", "Default User", "All Users" };
                    var userDirs = Directory.GetDirectories(usersUncPath)
                        .Where(d => !skip.Contains(Path.GetFileName(d)))
                        .ToList();

                    // Tüm profil yerine sadece bilinen büyük alt klasörleri tara
                    var userSubfolders = new (string Rel, string Cat)[]
                    {
                        (@"AppData\Local\Temp",                                          "temp"),
                        (@"AppData\Local\Microsoft\Windows\INetCache",                   "cache"),
                        (@"AppData\Local\Microsoft\Windows\WebCache",                    "cache"),
                        (@"AppData\Local\Google\Chrome\User Data\Default\Cache",         "cache"),
                        (@"AppData\Local\Google\Chrome\User Data\Default\Cache2",        "cache"),
                        (@"AppData\Local\Microsoft\Edge\User Data\Default\Cache",        "cache"),
                        (@"AppData\Local\Microsoft\Edge\User Data\Default\Cache2",       "cache"),
                        (@"AppData\LocalLow\Microsoft\CryptnetUrlCache",                 "cache"),
                        (@"AppData\Local\CrashDumps",                                   "dumps"),
                    };

                    var userSubTasks = userDirs.SelectMany(userDir =>
                    {
                        var userName = Path.GetFileName(userDir);
                        return userSubfolders.Select(async sub =>
                        {
                            var subPath = Path.Combine(userDir, sub.Rel);
                            try { if (!Directory.Exists(subPath)) return; } catch { return; }
                            if (globalCts.Token.IsCancellationRequested) return;

                            await sem.WaitAsync(globalCts.Token);
                            try
                            {
                                using var localCts = CancellationTokenSource.CreateLinkedTokenSource(globalCts.Token);
                                localCts.CancelAfter(15_000);

                                var (bytes, count, timedOut) = await ScanFolderAsync(subPath, localCts.Token);
                                if (bytes == 0 && !timedOut) return;
                                folders.Add(new FolderSizeItem
                                {
                                    Name = $@"Users\{userName}\{sub.Rel}",
                                    LocalPath = $@"C:\Users\{userName}\{sub.Rel}",
                                    SizeGB = Math.Round(bytes / (1024.0 * 1024 * 1024), 2),
                                    FileCount = count,
                                    TimedOut = timedOut,
                                    Category = sub.Cat
                                });
                            }
                            catch (OperationCanceledException) { }
                            finally { sem.Release(); }
                        });
                    }).ToList();

                    usersTask = Task.WhenAll(userSubTasks);
                }
            }
            catch { }

            // SQL Server error log dizinlerini tara
            var sqlScanTask = Task.Run(async () =>
            {
                try
                {
                    var sqlBases = new[]
                    {
                        Path.Combine(uncRoot, @"Program Files\Microsoft SQL Server"),
                        Path.Combine(uncRoot, @"Program Files (x86)\Microsoft SQL Server")
                    };
                    foreach (var sqlBase in sqlBases)
                    {
                        if (!Directory.Exists(sqlBase)) continue;
                        foreach (var mssqlDir in Directory.GetDirectories(sqlBase, "MSSQL*"))
                        {
                            var logDir = Path.Combine(mssqlDir, "MSSQL", "Log");
                            if (!Directory.Exists(logDir) || globalCts.Token.IsCancellationRequested) continue;
                            await sem.WaitAsync(globalCts.Token);
                            try
                            {
                                using var localCts = CancellationTokenSource.CreateLinkedTokenSource(globalCts.Token);
                                localCts.CancelAfter(10_000);
                                var (bytes, count, timedOut) = await ScanFolderAsync(logDir, localCts.Token);
                                folders.Add(new FolderSizeItem
                                {
                                    Name = $"SQL Log ({Path.GetFileName(mssqlDir)})",
                                    LocalPath = logDir.Replace(uncRoot, "C:"),
                                    SizeGB = Math.Round(bytes / (1024.0 * 1024 * 1024), 2),
                                    FileCount = count,
                                    TimedOut = timedOut,
                                    Category = "sql"
                                });
                            }
                            catch (OperationCanceledException) { }
                            finally { sem.Release(); }
                        }
                    }
                }
                catch { }
            });

            try { await Task.WhenAll(targetTasks.Append(usersTask).Append(sqlScanTask)); } catch { }

            result.Folders = folders
                .Where(f => f.SizeGB > 0 || f.TimedOut)
                .OrderByDescending(f => f.SizeGB)
                .ToList();

            _logger.LogInformation("Disk analizi tamamlandı: {DeviceId} → {Count} klasör, top: {Top}",
                deviceId, result.Folders.Count,
                result.Folders.FirstOrDefault()?.Name ?? "-");

            return Ok(result);
        }

        private static async Task<(long bytes, long count, bool timedOut)> ScanFolderAsync(
            string path, CancellationToken ct)
        {
            // Task.Run'a ct vermiyoruz — EnumerateFiles SMB'de ağ I/O'da takılırsa
            // CancellationToken onu durduramaz. Task.WhenAny ile hard timeout kullanıyoruz.
            var scanTask = Task.Run(() =>
            {
                long bytes = 0, count = 0;
                var opts = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                foreach (var fi in new DirectoryInfo(path).EnumerateFiles("*", opts))
                {
                    if (ct.IsCancellationRequested) return (bytes, count, true);
                    bytes += fi.Length;
                    count++;
                }
                return (bytes, count, false);
            });

            try
            {
                // Task.Delay(ct) → global CTS iptal edince hemen döner
                var timeoutTask = Task.Delay(15_000, ct);
                var winner = await Task.WhenAny(scanTask, timeoutTask);
                if (winner == scanTask && scanTask.IsCompletedSuccessfully)
                    return scanTask.Result;
                return (0, 0, true); // zaman aşımı veya iptal
            }
            catch { return (0, 0, true); }
        }

        // ==================== CLEANUP ENDPOINTS ====================

        public class DiskCleanupRequest
        {
            /// <summary>windows-temp | software-distribution | folder</summary>
            public string CleanupType { get; set; } = "";
            /// <summary>CleanupType == "folder" ise hedef yol</summary>
            public string? FolderPath { get; set; }
        }

        /// <summary>
        /// SMB üzerinden klasör temizleme — agent gerekmez.
        /// POST: /api/disk-status/cleanup/{deviceId}
        /// </summary>
        [HttpPost("cleanup/{deviceId}")]
        public async Task<IActionResult> CleanupFolder(string deviceId, [FromBody] DiskCleanupRequest request)
        {
            var storeDevice = await _db.StoreDevices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (storeDevice == null) return NotFound(new { error = "Device not found" });

            var localPath = request.CleanupType switch
            {
                "windows-temp"          => @"C:\Windows\Temp",
                "software-distribution" => @"C:\Windows\SoftwareDistribution\Download",
                "folder"                => request.FolderPath,
                _                       => null
            };
            if (string.IsNullOrWhiteSpace(localPath))
                return BadRequest(new { error = "Geçersiz cleanupType veya boş folderPath." });

            // "C:\Windows\Temp" → "\\ip\C$\Windows\Temp"
            var driveLetter = char.ToUpper(localPath[0]);
            var uncPath = $@"\\{storeDevice.CalculatedIpAddress}\{driveLetter}$" + localPath[2..];

            int deleted = 0;
            long totalBytes = 0;
            var errors = new List<string>();

            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(uncPath)) { errors.Add("Klasör bulunamadı: " + uncPath); return; }

                    foreach (var file in Directory.GetFiles(uncPath, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            long sz = new FileInfo(file).Length;
                            System.IO.File.Delete(file);
                            totalBytes += sz;
                            deleted++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add(Path.GetFileName(file) + ": " + ex.Message[..Math.Min(60, ex.Message.Length)]);
                        }
                    }
                    // Boş alt klasörleri sil
                    foreach (var dir in Directory.GetDirectories(uncPath, "*", SearchOption.AllDirectories)
                                                 .OrderByDescending(d => d.Length))
                        try { Directory.Delete(dir); } catch { }
                }
                catch (Exception ex) { errors.Add(ex.Message[..Math.Min(80, ex.Message.Length)]); }
            });

            _logger.LogInformation("SMB FolderCleanup: {Path} → {Count} dosya, {MB} MB", uncPath, deleted, Math.Round(totalBytes / (1024.0 * 1024), 1));
            return Ok(new { deletedCount = deleted, freedMB = Math.Round(totalBytes / (1024.0 * 1024), 1), errors });
        }

        // SQL log temizleme scripti:
        //   1. Genius POS uygulamasını kapat  2. SQL durdur  3. Logları sil  4. SQL başlat  5. POS başlat
        private const string SqlLogCleanupScript = """
$ErrorActionPreference = 'Continue'
$log = @()

# ─── 1. Genius POS süreçlerini bul ve kapat ────────────────────────────────
$posProcs = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    ($_.Name -like "*Genius*") -or
    ($_.Name -like "*GeniusOpen*") -or
    ($_.Name -like "*GeniusPOS*") -or
    (try { $_.Path -like "*Genius*" } catch { $false })
}
$posProcs = @($posProcs | Sort-Object Id -Unique)
$posExePaths = @()

foreach ($p in $posProcs) {
    try {
        $exePath = $p.Path
        if ($exePath -and (Test-Path $exePath)) { $posExePaths += $exePath }
        $log += "POS kapatiliyor: $($p.Name) (PID $($p.Id))"
        $p.CloseMainWindow() | Out-Null
    } catch {}
}
if ($posProcs.Count -gt 0) { Start-Sleep 4 }

# Kapanmayanları zorla kapat
foreach ($p in $posProcs) {
    try { $p.Refresh(); if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } } catch {}
}

# ─── 2. SQL Server log dizinlerini bul ─────────────────────────────────────
$sqlBases = @("C:\Program Files\Microsoft SQL Server","C:\Program Files (x86)\Microsoft SQL Server")
$sqlLogDirs = @()
foreach ($base in $sqlBases) {
    if (-not (Test-Path $base)) { continue }
    Get-ChildItem $base -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "MSSQL*" } |
        ForEach-Object {
            $ld = Join-Path $_.FullName "MSSQL\Log"
            if (Test-Path $ld) { $sqlLogDirs += $ld }
        }
}
if ($sqlLogDirs.Count -eq 0) { $log += "SQL log dizini bulunamadi."; $log -join "`n"; exit 0 }

# ─── 3. SQL servislerini durdur ─────────────────────────────────────────────
$sqlSvcs = @(Get-Service -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match "^MSSQL" -and $_.Status -eq 'Running' })
if ($sqlSvcs.Count -eq 0) { $log += "Calisan SQL servisi bulunamadi."; $log -join "`n"; exit 0 }

$log += "SQL durduruluyor: $($sqlSvcs.Name -join ', ')"
$sqlSvcs | ForEach-Object { Stop-Service $_.Name -Force -ErrorAction SilentlyContinue }
Start-Sleep 6

# ─── 4. Log dosyalarını sil ─────────────────────────────────────────────────
$totalCount = 0; $totalMB = 0.0
foreach ($dir in $sqlLogDirs) {
    Get-ChildItem $dir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "^(ERRORLOG|SQLAGENT)" } |
        ForEach-Object {
            $mb = [math]::Round($_.Length / 1MB, 2)
            Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
            $totalCount++; $totalMB += $mb
            $log += "Silindi: $($_.Name) ($mb MB)"
        }
}

# ─── 5. SQL servislerini yeniden başlat ─────────────────────────────────────
$log += "SQL baslatiliyor: $($sqlSvcs.Name -join ', ')"
$sqlSvcs | ForEach-Object { Start-Service $_.Name -ErrorAction SilentlyContinue }
Start-Sleep 8

$runOk = 0
foreach ($svc in $sqlSvcs) {
    $s = (Get-Service $svc.Name -ErrorAction SilentlyContinue).Status
    if ($s -eq 'Running') { $runOk++ }
    $log += "SQL servis: $($svc.Name) = $s"
}

# ─── 6. Genius POS'u yeniden başlat ─────────────────────────────────────────
$posRestarted = 0
foreach ($path in ($posExePaths | Select-Object -Unique)) {
    try {
        if (Test-Path $path) {
            Start-Process -FilePath $path -WorkingDirectory (Split-Path $path)
            $posRestarted++
            $log += "POS baslatildi: $(Split-Path $path -Leaf)"
        }
    } catch { $log += "POS baslatilamadi: $($_.Exception.Message)" }
}
if ($posRestarted -eq 0 -and $posProcs.Count -gt 0) {
    $log += "UYARI: POS uygulamasi otomatik baslatilamadi, elle acilmasi gerekebilir."
}

# ─── Sonuç ──────────────────────────────────────────────────────────────────
$log += "---"
$log += "Sonuc: $totalCount dosya silindi, $([math]::Round($totalMB,2)) MB kazanildi"
$log += "SQL: $runOk/$($sqlSvcs.Count) aktif | POS: $posRestarted yeniden baslatildi"
$log -join "`n"
""";

        /// <summary>
        /// SQL Server arşiv loglarını SMB üzerinden doğrudan sil (servis durmadan).
        /// ERRORLOG.1, ERRORLOG.2 … ve SQLAGENT.1 … aktif değildir, silinebilir.
        /// POST: /api/disk-status/cleanup-sql-logs/{deviceId}
        /// </summary>
        [HttpPost("cleanup-sql-logs/{deviceId}")]
        public async Task<IActionResult> CleanupSqlLogs(string deviceId)
        {
            var storeDevice = await _db.StoreDevices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (storeDevice == null) return NotFound(new { error = "Device not found" });

            var ip = storeDevice.CalculatedIpAddress;
            var uncRoot = $@"\\{ip}\C$";

            var deleted = new List<string>();
            var errors  = new List<string>();
            long totalBytes = 0;

            var sqlBases = new[]
            {
                Path.Combine(uncRoot, @"Program Files\Microsoft SQL Server"),
                Path.Combine(uncRoot, @"Program Files (x86)\Microsoft SQL Server")
            };

            await Task.Run(() =>
            {
                foreach (var sqlBase in sqlBases)
                {
                    try { if (!Directory.Exists(sqlBase)) continue; } catch { continue; }

                    foreach (var mssqlDir in Directory.GetDirectories(sqlBase, "MSSQL*"))
                    {
                        var logDir = Path.Combine(mssqlDir, "MSSQL", "Log");
                        try { if (!Directory.Exists(logDir)) continue; } catch { continue; }

                        foreach (var file in Directory.GetFiles(logDir, "*", SearchOption.AllDirectories))
                        {
                            var name = Path.GetFileName(file);
                            var ext  = Path.GetExtension(file).ToLowerInvariant();

                            // Database dosyalarına ASLA dokunma
                            if (ext is ".mdf" or ".ldf" or ".ndf" or ".bak" or ".bk") continue;

                            // Silinebilir: numbered ERRORLOG/SQLAGENT, *.trc, *.xel (extended events), *.mdmp (memory dumps)
                            // Aktif dosyalar (ERRORLOG, SQLAGENT.OUT, aktif .trc/.xel) kilitlidir — try/catch yakar
                            bool isDeletable =
                                (name.StartsWith("ERRORLOG.", StringComparison.OrdinalIgnoreCase) &&
                                 name.Length > "ERRORLOG.".Length) ||
                                (name.StartsWith("SQLAGENT.", StringComparison.OrdinalIgnoreCase) &&
                                 !name.Equals("SQLAGENT.OUT", StringComparison.OrdinalIgnoreCase)) ||
                                ext is ".trc" or ".xel" or ".mdmp";

                            if (!isDeletable) continue;

                            try
                            {
                                long fileSize = new FileInfo(file).Length;
                                System.IO.File.Delete(file); // fi.Delete() UNC path'te güvenilmez
                                totalBytes += fileSize;
                                deleted.Add($"{name} ({Math.Round(fileSize / (1024.0 * 1024), 1)} MB)");
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"{name}: {ex.Message[..Math.Min(80, ex.Message.Length)]}");
                            }
                        }
                    }
                }
            });

            var freedMB = Math.Round(totalBytes / (1024.0 * 1024), 1);
            _logger.LogInformation("SQL log temizleme: {DeviceId} → {Count} dosya, {MB} MB", deviceId, deleted.Count, freedMB);

            return Ok(new { deletedCount = deleted.Count, freedMB, files = deleted, errors });
        }

        // =====================================================================

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
