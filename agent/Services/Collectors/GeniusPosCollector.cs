using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using System.ServiceProcess;
using System.Text.Json;

namespace Orchestra.Agent.Services.Collectors;

/// <summary>
/// Genius POS'a özgü sağlık verilerini toplar.
/// Kasa PC'lerinde çalışır; servis durumu, versiyon, DB bağlantısı,
/// hata sayacı ve disk birikimlerini raporlar.
/// </summary>
public class GeniusPosCollector : ICollector
{
    private readonly ILogger<GeniusPosCollector> _logger;
    private readonly CollectorsConfig _config;

    private static readonly string[] PosServiceNames =
    {
        "GeniusPOS", "Genius3POS", "GeniusService", "GeniusPosService",
        "GeniusPOSService", "POS", "MudoPOS"
    };

    private static readonly string[] PosDataPaths =
    {
        @"C:\Genius3\",
        @"D:\Genius3\",
        @"C:\Genius\",
        @"D:\Genius\"
    };

    public string Name => "GeniusPos";
    public TimeSpan Interval => TimeSpan.FromMinutes(15);
    public bool Enabled => _config.GeniusPos?.Enabled ?? true;

    public GeniusPosCollector(
        ILogger<GeniusPosCollector> logger,
        IOptions<CollectorsConfig> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    public async Task<CollectorResult> CollectAsync(CancellationToken ct)
    {
        var data = new GeniusPosData();
        var severity = "Info";

        try
        {
            // 1. POS Service statuses
            data.Services = GetPosServiceStatuses();

            // 2. parameters.bat — JRE_HOME ve version bilgisi
            var parametersInfo = ReadParametersBat();
            data.JreHome = parametersInfo.JreHome;
            data.PosVersion = parametersInfo.PosVersion;

            // 3. DB connection test + sorunlu kayıt sayıları
            var dbInfo = await QueryPosDatabase(parametersInfo.ConnectionString, ct);
            data.DbConnectable = dbInfo.Connected;
            data.SqlVersion = dbInfo.SqlVersion;
            data.StockTransferErrorCount = dbInfo.StockTransferErrors;
            data.ExportErrLogCount = dbInfo.ExportErrLogCount;
            data.LastSuccessfulTransferAt = dbInfo.LastSuccessfulTransfer;

            // 4. Disk accumulation — Seq / XML files
            var diskInfo = ScanPosDataDisk();
            data.SeqFileCount = diskInfo.SeqCount;
            data.XmlFileCount = diskInfo.XmlCount;
            data.SeqXmlTotalMB = diskInfo.TotalMB;
            data.PosDataPath = diskInfo.FoundPath;

            // Determine severity
            if (!data.DbConnectable || data.Services.Any(s => s.Status == "Stopped"))
                severity = "Critical";
            else if (data.StockTransferErrorCount > 100 || data.ExportErrLogCount > 50 || data.SeqFileCount > 500)
                severity = "Warning";
            else
                severity = "Info";

            data.CollectedAt = DateTime.UtcNow;

            return new CollectorResult
            {
                CollectorName = Name,
                Severity = severity,
                JsonData = JsonSerializer.Serialize(data),
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeniusPos collector failed");
            return new CollectorResult
            {
                CollectorName = Name,
                Severity = "Warning",
                JsonData = JsonSerializer.Serialize(data),
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // ── Service Status ──────────────────────────────────────────────────

    private List<PosServiceStatus> GetPosServiceStatuses()
    {
        var result = new List<PosServiceStatus>();
        foreach (var svcName in PosServiceNames)
        {
            try
            {
                using var sc = new ServiceController(svcName);
                _ = sc.Status; // triggers exception if not found
                result.Add(new PosServiceStatus
                {
                    ServiceName = svcName,
                    Status = sc.Status.ToString()
                });
            }
            catch (InvalidOperationException)
            {
                // Service not installed — skip
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Service {Name} query failed: {Msg}", svcName, ex.Message);
            }
        }
        return result;
    }

    // ── parameters.bat ───────────────────────────────────────────────────

    private (string? JreHome, string? PosVersion, string? ConnectionString) ReadParametersBat()
    {
        string? jreHome = null;
        string? posVersion = null;
        string? connStr = null;

        foreach (var basePath in PosDataPaths)
        {
            var candidates = new[]
            {
                Path.Combine(basePath, "parameters.bat"),
                Path.Combine(basePath, "bin", "parameters.bat"),
                Path.Combine(basePath, "config", "parameters.bat")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (line.TrimStart().StartsWith("set JRE_HOME=", StringComparison.OrdinalIgnoreCase))
                            jreHome = line.Split('=', 2).Last().Trim();
                        if (line.TrimStart().StartsWith("set VERSION=", StringComparison.OrdinalIgnoreCase) ||
                            line.TrimStart().StartsWith("set POS_VERSION=", StringComparison.OrdinalIgnoreCase))
                            posVersion = line.Split('=', 2).Last().Trim();
                        if (line.TrimStart().StartsWith("set DB_SERVER=", StringComparison.OrdinalIgnoreCase) ||
                            line.TrimStart().StartsWith("set SQL_SERVER=", StringComparison.OrdinalIgnoreCase))
                        {
                            var dbServer = line.Split('=', 2).Last().Trim();
                            if (!string.IsNullOrWhiteSpace(dbServer))
                                connStr = $"Server={dbServer};Database=Genius3;TrustServerCertificate=True;Connect Timeout=5;";
                        }
                    }
                    if (jreHome != null || posVersion != null) break;
                }
                catch { /* skip unreadable files */ }
            }
        }

        // Fallback connection string — assume SQL on localhost
        connStr ??= "Server=127.0.0.1;Database=Genius3;TrustServerCertificate=True;Connect Timeout=5;";

        return (jreHome, posVersion, connStr);
    }

    // ── Database Queries ─────────────────────────────────────────────────

    private async Task<DbInfo> QueryPosDatabase(string? connectionString, CancellationToken ct)
    {
        var info = new DbInfo();
        if (string.IsNullOrEmpty(connectionString)) return info;

        // Try Windows Auth first, then look for env var credentials
        var connStrings = new List<string>
        {
            connectionString + "Integrated Security=True;",
            connectionString // will fail without auth — skip gracefully
        };

        // If env vars are set, add credential-based variant
        var dbUser = Environment.GetEnvironmentVariable("GENIUS_DB_USER");
        var dbPass = Environment.GetEnvironmentVariable("GENIUS_DB_PASSWORD");
        if (!string.IsNullOrEmpty(dbUser) && !string.IsNullOrEmpty(dbPass))
            connStrings.Insert(0, connectionString + $"User Id={dbUser};Password={dbPass};");

        foreach (var cs in connStrings)
        {
            try
            {
                await using var conn = new SqlConnection(cs);
                await conn.OpenAsync(ct);
                info.Connected = true;

                // SQL version
                await using var verCmd = conn.CreateCommand();
                verCmd.CommandText = "SELECT @@VERSION";
                verCmd.CommandTimeout = 5;
                var ver = await verCmd.ExecuteScalarAsync(ct) as string;
                info.SqlVersion = ver?.Split('\n').FirstOrDefault()?.Trim();

                // POS_STOCK_TRANSFER error rows (STATUS <> 'S' and STATUS <> 'P')
                await using var stockCmd = conn.CreateCommand();
                stockCmd.CommandText = @"
                    SELECT COUNT(*) FROM POS_STOCK_TRANSFER
                    WHERE STATUS NOT IN ('S','P','C')
                    AND TRANSFER_DATE >= DATEADD(day,-7,GETDATE())";
                stockCmd.CommandTimeout = 5;
                try
                {
                    var stockResult = await stockCmd.ExecuteScalarAsync(ct);
                    info.StockTransferErrors = Convert.ToInt32(stockResult);
                }
                catch { /* table may not exist in all versions */ }

                // EXPORT_ERR_LOG count
                await using var errCmd = conn.CreateCommand();
                errCmd.CommandText = @"
                    SELECT COUNT(*) FROM EXPORT_ERR_LOG
                    WHERE LOG_DATE >= DATEADD(day,-1,GETDATE())";
                errCmd.CommandTimeout = 5;
                try
                {
                    var errResult = await errCmd.ExecuteScalarAsync(ct);
                    info.ExportErrLogCount = Convert.ToInt32(errResult);
                }
                catch { /* table may not exist */ }

                // Last successful transfer
                await using var lastCmd = conn.CreateCommand();
                lastCmd.CommandText = @"
                    SELECT TOP 1 TRANSFER_DATE FROM POS_STOCK_TRANSFER
                    WHERE STATUS IN ('S','C')
                    ORDER BY TRANSFER_DATE DESC";
                lastCmd.CommandTimeout = 5;
                try
                {
                    var lastResult = await lastCmd.ExecuteScalarAsync(ct);
                    if (lastResult != null && lastResult != DBNull.Value)
                        info.LastSuccessfulTransfer = Convert.ToDateTime(lastResult);
                }
                catch { /* skip */ }

                break; // success — no need to try other connection strings
            }
            catch (SqlException ex) when (ex.Number == 18456) // auth failure
            {
                _logger.LogDebug("SQL auth failed with connection string variant");
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SQL connection failed: {Msg}", ex.Message);
            }
        }

        return info;
    }

    // ── Disk Scan ────────────────────────────────────────────────────────

    private DiskScanResult ScanPosDataDisk()
    {
        var result = new DiskScanResult();

        foreach (var basePath in PosDataPaths)
        {
            var seqPaths = new[]
            {
                Path.Combine(basePath, "data", "seq"),
                Path.Combine(basePath, "seq"),
                Path.Combine(basePath, "SATIS")
            };
            var xmlPaths = new[]
            {
                Path.Combine(basePath, "data", "xml"),
                Path.Combine(basePath, "xml"),
                Path.Combine(basePath, "EXPORT")
            };

            bool found = false;
            foreach (var p in seqPaths.Concat(xmlPaths))
            {
                if (Directory.Exists(p)) { found = true; break; }
            }
            if (!found) continue;

            result.FoundPath = basePath;

            foreach (var p in seqPaths)
            {
                if (!Directory.Exists(p)) continue;
                try
                {
                    var files = Directory.GetFiles(p, "*.seq", SearchOption.TopDirectoryOnly);
                    result.SeqCount += files.Length;
                    result.TotalMB += files.Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0;
                }
                catch { }
            }

            foreach (var p in xmlPaths)
            {
                if (!Directory.Exists(p)) continue;
                try
                {
                    var files = Directory.GetFiles(p, "*.xml", SearchOption.TopDirectoryOnly);
                    result.XmlCount += files.Length;
                    result.TotalMB += files.Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0;
                }
                catch { }
            }

            break;
        }

        result.TotalMB = Math.Round(result.TotalMB, 2);
        return result;
    }

    // ── Private DTOs ─────────────────────────────────────────────────────

    private class DbInfo
    {
        public bool Connected { get; set; }
        public string? SqlVersion { get; set; }
        public int StockTransferErrors { get; set; }
        public int ExportErrLogCount { get; set; }
        public DateTime? LastSuccessfulTransfer { get; set; }
    }

    private class DiskScanResult
    {
        public string? FoundPath { get; set; }
        public int SeqCount { get; set; }
        public int XmlCount { get; set; }
        public double TotalMB { get; set; }
    }
}

// ── Public DTOs (serialized to JSON for CollectorReport) ─────────────────

public class GeniusPosData
{
    public DateTime CollectedAt { get; set; }
    public List<PosServiceStatus> Services { get; set; } = new();
    public string? JreHome { get; set; }
    public string? PosVersion { get; set; }
    public string? SqlVersion { get; set; }
    public bool DbConnectable { get; set; }
    public int StockTransferErrorCount { get; set; }
    public int ExportErrLogCount { get; set; }
    public DateTime? LastSuccessfulTransferAt { get; set; }
    public int SeqFileCount { get; set; }
    public int XmlFileCount { get; set; }
    public double SeqXmlTotalMB { get; set; }
    public string? PosDataPath { get; set; }
}

public class PosServiceStatus
{
    public string ServiceName { get; set; } = "";
    public string Status { get; set; } = "";
}
