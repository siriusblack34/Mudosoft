using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;
using Orchestra.Shared.Dtos;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    private const string AgentServiceName = "MudosoftAgentService";
    private const string AgentServiceDisplayName = "Orchestra Agent Service";
    private const string AgentExecutablePath = @"C:\Program Files\MudoSoft\Agent\MudoSoft.Agent.exe";
    private readonly IDeviceRepository _repo;
    private readonly OrchestraDbContext _dbContext;
    private readonly ILogger<DevicesController> _logger;
    private static readonly ConcurrentDictionary<string, OfflineServiceStartJob> _startJobs = new();

    public DevicesController(
        IDeviceRepository repo,
        OrchestraDbContext dbContext,
        ILogger<DevicesController> logger)
    {
        _repo = repo;
        _dbContext = dbContext;
        _logger = logger;
    }

    private int? SafeRoundToNullableInt(float rawValue)
    {
        if (float.IsNaN(rawValue) || float.IsInfinity(rawValue))
        {
            return null;
        }

        var roundedValue = (int)Math.Round(rawValue);
        return roundedValue > 0 ? roundedValue : null;
    }

    private static bool IsIgnoredForOfflineTracking(Device device)
    {
        return device.ExcludeFromOfflineList || device.IsTemporarilyClosed;
    }

    [HttpGet("status")]
    public ActionResult<DashboardDto> GetStatus()
    {
        var isAdmin = User.IsInRole("Admin");
        var devices = _repo.GetAll()
            .Where(d => !string.IsNullOrEmpty(d.AgentVersion))
            .Where(d => isAdmin || !d.HiddenForNonAdmins)
            .ToList();

        var total = devices.Count;
        var online = devices.Count(d => d.Online);
        var offline = devices.Count(d => !d.Online && !IsIgnoredForOfflineTracking(d));

        var recentOffline = devices
            .Where(d => !d.Online && !IsIgnoredForOfflineTracking(d))
            .OrderByDescending(d => d.LastSeen)
            .Take(10)
            .Select(d => new RecentOfflineDevice
            {
                Hostname = d.Hostname ?? "-",
                Ip = d.IpAddress,
                Os = d.Os ?? "-",
                Store = d.StoreCode,
                LastSeen = d.LastSeen?.ToString("g") ?? "-"
            })
            .ToList();

        return Ok(new DashboardDto
        {
            TotalDevices = total,
            Online = online,
            Offline = offline,
            RecentOffline = recentOffline
        });
    }

    [HttpGet("inventory")]
    public async Task<ActionResult<IEnumerable<DeviceListDto>>> GetInventory()
    {
        var isAdmin = User.IsInRole("Admin");
        var devices = _repo.GetAll()
            .Where(d => !string.IsNullOrEmpty(d.AgentVersion))
            .Where(d => isAdmin || !d.HiddenForNonAdmins)
            .ToList();

        var storeNames = await _dbContext.StoreDevices
            .AsNoTracking()
            .Select(sd => new { sd.StoreCode, sd.StoreName })
            .Distinct()
            .ToListAsync();

        var storeNameMap = storeNames
            .GroupBy(s => s.StoreCode)
            .ToDictionary(g => g.Key, g => g.First().StoreName);

        var deviceDtos = devices.Select(d =>
        {
            var storeCode = d.StoreCode;
            // device.StoreCode yoksa (0) IP'den çıkar — fallback. Non-standart subnet'li mağazalarda
            // (ör. 258 → 192.168.240.x) device.StoreCode'a güven, IP'yi override etme.
            if (storeCode <= 0
                && !string.IsNullOrEmpty(d.IpAddress)
                && d.IpAddress.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase))
            {
                var parts = d.IpAddress.Split('.');
                if (parts.Length == 4 && int.TryParse(parts[2], out var ipStore) && ipStore > 0)
                {
                    storeCode = ipStore;
                }
            }

            var osName = ParseOsShortName(d.Os);
            storeNameMap.TryGetValue(storeCode, out var storeName);

            return new DeviceListDto
            {
                Id = d.Id,
                Hostname = d.Hostname,
                IpAddress = d.IpAddress,
                Os = new OsInfoDto { Name = osName },
                StoreCode = storeCode,
                StoreName = storeName,
                Type = d.Type.ToString(),
                Online = d.Online,
                ExcludeFromOfflineList = d.ExcludeFromOfflineList,
                IsTemporarilyClosed = d.IsTemporarilyClosed,
                TemporaryCloseReason = d.TemporaryCloseReason,
                LastSeen = d.LastSeen?.ToString("o"),
                CpuUsage = SafeRoundToNullableInt(d.CurrentCpuUsagePercent),
                RamUsage = SafeRoundToNullableInt(d.CurrentRamUsagePercent),
                DiskUsage = SafeRoundToNullableInt(d.CurrentDiskUsagePercent),
                HiddenForNonAdmins = d.HiddenForNonAdmins
            };
        }).ToList();

        return Ok(deviceDtos);
    }

    /// <summary>
    /// Cihazın non-admin görünürlüğünü toggle eder. Sadece Admin.
    /// HiddenForNonAdmins=true → non-admin'ler listede görmez.
    /// </summary>
    [HttpPut("{id}/visibility")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetVisibility(string id, [FromBody] SetVisibilityRequest req)
    {
        var device = await _dbContext.Devices.FindAsync(id);
        if (device == null) return NotFound();
        device.HiddenForNonAdmins = req.Hidden;
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("[Visibility] {Host} ({Id}) HiddenForNonAdmins -> {Hidden} by {User}",
            device.Hostname, id, req.Hidden, User?.Identity?.Name);
        return Ok(new { id, hiddenForNonAdmins = device.HiddenForNonAdmins });
    }

    public class SetVisibilityRequest
    {
        public bool Hidden { get; set; }
    }

    [HttpPost("start-offline-services")]
    public ActionResult<StartOfflineServicesResponse> StartOfflineServices()
    {
        var offlineDevices = _repo.GetAll()
            .Where(d =>
                !d.Online &&
                !IsIgnoredForOfflineTracking(d) &&
                !string.IsNullOrWhiteSpace(d.AgentVersion) &&
                !string.IsNullOrWhiteSpace(d.IpAddress))
            .OrderBy(d => d.StoreCode)
            .ThenBy(d => d.Hostname)
            .ToList();

        var targets = offlineDevices
            .Select(d => new StartOfflineServiceResult
            {
                DeviceId = d.Id,
                Hostname = d.Hostname ?? "-",
                IpAddress = d.IpAddress ?? "",
                StoreCode = d.StoreCode,
                Status = "queued",
                Message = "Servis baslatma komutu kuyruga alindi."
            })
            .OrderBy(r => r.StoreCode)
            .ThenBy(r => r.Hostname)
            .ToList();

        var jobId = Guid.NewGuid().ToString("N")[..8];
        var job = new OfflineServiceStartJob
        {
            JobId = jobId,
            TotalOffline = targets.Count,
            StartedAtUtc = DateTime.UtcNow,
            Results = targets
        };

        _startJobs[jobId] = job;
        _ = ProcessOfflineServiceStartJobAsync(jobId, offlineDevices);

        _logger.LogInformation(
            "Offline servis start job queued: JobId={JobId} TargetCount={Count} Targets={Targets}",
            jobId,
            targets.Count,
            string.Join(", ", targets.Select(t => $"{t.Hostname}({t.IpAddress})")));

        return Accepted(new StartOfflineServicesResponse
        {
            JobId = jobId,
            TotalOffline = job.TotalOffline,
            Attempted = 0,
            PingReachable = 0,
            StartIssued = 0,
            RunningConfirmed = 0,
            Results = targets
        });
    }

    [HttpGet("start-offline-services/{jobId}")]
    public IActionResult GetStartOfflineServicesStatus(string jobId)
    {
        if (_startJobs.TryGetValue(jobId, out var job))
        {
            return Ok(new StartOfflineServicesResponse
            {
                JobId = job.JobId,
                TotalOffline = job.TotalOffline,
                Attempted = job.Results.Count,
                PingReachable = job.Results.Count(r => r.PingReachable),
                StartIssued = job.Results.Count(r => r.StartIssued),
                RunningConfirmed = job.Results.Count(r => r.RunningConfirmed),
                CompletedAtUtc = job.CompletedAtUtc?.ToString("o"),
                Results = job.Results.OrderBy(r => r.StoreCode).ThenBy(r => r.Hostname).ToList()
            });
        }

        return NotFound(new { error = "Job bulunamadi" });
    }

    private static string ParseOsShortName(string? os)
    {
        if (string.IsNullOrEmpty(os))
        {
            return "-";
        }

        var normalized = os.Replace("Microsoft ", "", StringComparison.OrdinalIgnoreCase);
        var dashIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex > 0)
        {
            normalized = normalized[..dashIndex];
        }

        var buildIndex = normalized.IndexOf(" Build ", StringComparison.OrdinalIgnoreCase);
        if (buildIndex > 0)
        {
            normalized = normalized[..buildIndex];
        }

        return normalized
            .Replace("Professional", "Pro", StringComparison.OrdinalIgnoreCase)
            .Replace("Enterprise", "Ent", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DeviceDetailDto>> GetById(string id)
    {
        var device = _repo.GetById(id);
        if (device is null)
        {
            return NotFound($"Device not found: {id}");
        }

        var osInfoLocal = new OsInfoDto();
        if (!string.IsNullOrWhiteSpace(device.Os))
        {
            var osString = device.Os;
            var firstSpaceIndex = osString.IndexOf(' ');

            if (firstSpaceIndex > 0)
            {
                osInfoLocal.Name = osString[..firstSpaceIndex];
                osInfoLocal.Version = osString[(firstSpaceIndex + 1)..].Trim();
            }
            else
            {
                osInfoLocal.Name = osString;
                osInfoLocal.Version = "-";
            }
        }
        else
        {
            osInfoLocal.Name = "Unknown";
            osInfoLocal.Version = "-";
        }

        var storeCode = device.StoreCode;
        if (!string.IsNullOrEmpty(device.IpAddress) && device.IpAddress.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase))
        {
            var ipParts = device.IpAddress.Split('.');
            if (ipParts.Length == 4 && int.TryParse(ipParts[2], out var ipStore) && ipStore > 0)
            {
                storeCode = ipStore;
            }
        }

        var storeNameEntry = await _dbContext.StoreDevices
            .AsNoTracking()
            .Where(sd => sd.StoreCode == storeCode)
            .Select(sd => sd.StoreName)
            .FirstOrDefaultAsync();

        return Ok(new DeviceDetailDto
        {
            Id = device.Id,
            Hostname = device.Hostname,
            IpAddress = device.IpAddress,
            Os = osInfoLocal,
            StoreCode = storeCode,
            StoreName = storeNameEntry,
            AgentVersion = device.AgentVersion,
            Type = device.Type.ToString(),
            Online = device.Online,
            ExcludeFromOfflineList = device.ExcludeFromOfflineList,
            IsTemporarilyClosed = device.IsTemporarilyClosed,
            TemporaryCloseReason = device.TemporaryCloseReason,
            LastSeen = device.LastSeen?.ToString("o"),
            FirstSeen = device.FirstSeen.ToString("o"),
            CpuUsage = (int)Math.Round(device.CurrentCpuUsagePercent),
            RamUsage = (int)Math.Round(device.CurrentRamUsagePercent),
            DiskUsage = (int)Math.Round(device.CurrentDiskUsagePercent),
            CpuModel = device.CpuModel,
            TotalRamMB = device.TotalRamMB,
            TotalDiskGB = device.TotalDiskGB,
            GpuModel = device.GpuModel,
            SerialNumber = device.SerialNumber,
            LastLoggedInUser = device.LastLoggedInUser,
            SystemBootTime = device.SystemBootTime?.ToString("o"),
            SqlVersion = device.SqlVersion,
            PosVersion = device.PosVersion,
            Agent = !string.IsNullOrEmpty(device.AgentVersion),
            VncInstalled = device.VncInstalled,
            VncPort = device.VncPort
        });
    }

    [HttpPut("{id}/offline-exclusion")]
    public async Task<IActionResult> SetOfflineExclusion(string id, [FromBody] SetOfflineExclusionRequest request)
    {
        var device = await _dbContext.Devices.FirstOrDefaultAsync(d => d.Id == id);
        if (device == null)
        {
            return NotFound(new { error = "Device bulunamadi" });
        }

        device.ExcludeFromOfflineList = request.ExcludeFromOfflineList;
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            DeviceId = device.Id,
            device.ExcludeFromOfflineList
        });
    }

    [HttpPut("{id}/temporary-close")]
    public async Task<IActionResult> SetTemporaryClose(string id, [FromBody] SetTemporaryCloseRequest request)
    {
        var device = await _dbContext.Devices.FirstOrDefaultAsync(d => d.Id == id);
        if (device == null)
        {
            return NotFound(new { error = "Device bulunamadi" });
        }

        device.IsTemporarilyClosed = request.IsClosed;
        device.TemporaryCloseReason = request.IsClosed ? request.Reason?.Trim() : null;
        await _dbContext.SaveChangesAsync();

        // Cache'deki cihazı da güncelle (dashboard yanıp sönmesin)
        DeviceStatusWorker.UpdateCachedDevice(device.Id, d =>
        {
            d.IsTemporarilyClosed = device.IsTemporarilyClosed;
            d.TemporaryCloseReason = device.TemporaryCloseReason;
        });

        return Ok(new
        {
            DeviceId = device.Id,
            device.IsTemporarilyClosed,
            device.TemporaryCloseReason
        });
    }

    /// <summary>
    /// Tum online Windows cihazlarinin BIOS seri numarasini remote WMI ile cekip DB'ye yazar.
    /// Background service ayda 1 otomatik calistirir; bu endpoint manuel tetikleme icindir.
    /// </summary>
    [HttpPost("sync-serials")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SyncSerials(
        [FromServices] SerialNumberSyncService syncService,
        CancellationToken ct)
    {
        var result = await syncService.RunSyncAsync(ct);
        return Ok(new
        {
            skipped = result.Skipped,
            total = result.Total,
            updated = result.Updated,
            unchanged = result.Unchanged,
            failed = result.Failed,
            completedAtUtc = result.CompletedAtUtc?.ToString("o"),
            lastRunUtc = SerialNumberSyncService.LastRunUtc == DateTime.MinValue
                ? null
                : SerialNumberSyncService.LastRunUtc.ToString("o")
        });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteDevice(string id)
    {
        var device = await _dbContext.Devices.FirstOrDefaultAsync(d => d.Id == id);
        if (device == null)
        {
            return NotFound(new { error = "Device bulunamadi" });
        }

        await _dbContext.DeviceMetrics.Where(m => m.DeviceId == id).ExecuteDeleteAsync();
        await _dbContext.CommandResultRecords.Where(r => r.DeviceId == id).ExecuteDeleteAsync();
        await _dbContext.CollectorReports.Where(r => r.DeviceId == id).ExecuteDeleteAsync();
        await _dbContext.VncSessionLogs.Where(r => r.DeviceId == id).ExecuteDeleteAsync();

        _dbContext.Devices.Remove(device);
        await _dbContext.SaveChangesAsync();

        return Ok(new { success = true, deletedDeviceId = id });
    }

    [HttpGet("{id}/metrics")]
    public async Task<ActionResult<IEnumerable<DeviceMetricDto>>> GetDeviceMetrics(string id)
    {
        var device = _repo.GetById(id);
        if (device == null)
        {
            return NotFound($"Device not found: {id}");
        }

        var last24Hours = DateTime.UtcNow.AddHours(-24);

        var metrics = await _dbContext.DeviceMetrics
            .AsNoTracking()
            .Where(m => m.DeviceId == id && m.TimestampUtc >= last24Hours)
            .OrderByDescending(m => m.TimestampUtc)
            .Take(120)
            .OrderBy(m => m.TimestampUtc)
            .Select(m => new DeviceMetricDto
            {
                TimestampUtc = m.TimestampUtc.ToString("o"),
                CpuUsagePercent = m.CpuUsagePercent,
                RamUsagePercent = m.RamUsagePercent,
                DiskUsagePercent = m.DiskUsagePercent
            })
            .ToListAsync();

        return Ok(metrics);
    }

    private static async Task<bool> TestPingAsync(string ipAddress)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, 1500);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private async Task<RemoteServiceStartAttempt> TryStartRemoteAgentServiceAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var directResult = await TryStartWithScAsync(ipAddress, cancellationToken);
        if (directResult.RunningConfirmed || directResult.StartIssued)
        {
            return directResult;
        }

        return await TryStartWithScheduledTaskAsync(ipAddress, cancellationToken);
    }

    private async Task<RemoteServiceStartAttempt> TryStartWithScAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var queryOutput = await RunCommandCaptureAsync("sc.exe", $"\\\\{ipAddress} query {AgentServiceName}", cancellationToken);
        if (queryOutput.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new RemoteServiceStartAttempt
            {
                Status = "already-running",
                Message = "Servis zaten calisiyor.",
                StartIssued = false,
                RunningConfirmed = true
            };
        }

        // sc.exe query bile hard-fail dondurduyse (Access Denied, RPC kapali, vb)
        // konfig/start denemenin anlami yok — erken sebebi raporla
        var queryDiag = ClassifyRemoteOutput(queryOutput);
        if (queryDiag.IsHardFailure)
        {
            return new RemoteServiceStartAttempt
            {
                Status = queryDiag.Status,
                Message = queryDiag.Message,
                StartIssued = false,
                RunningConfirmed = false
            };
        }

        var repairOutput = await RunCommandCaptureAsync(
            "sc.exe",
            $"\\\\{ipAddress} config {AgentServiceName} binPath= \"\\\"{AgentExecutablePath}\\\" --service\" start= delayed-auto obj= LocalSystem",
            cancellationToken);
        await RunCommandCaptureAsync("sc.exe", $"\\\\{ipAddress} description {AgentServiceName} \"Orchestra Agent Service\"", cancellationToken);
        var startOutput = await RunCommandCaptureAsync("sc.exe", $"\\\\{ipAddress} start {AgentServiceName}", cancellationToken);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            var verifyOutput = await RunCommandCaptureAsync("sc.exe", $"\\\\{ipAddress} query {AgentServiceName}", cancellationToken);
            if (verifyOutput.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                verifyOutput.IndexOf("START_PENDING", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new RemoteServiceStartAttempt
                {
                    Status = "running",
                    Message = "Servis uzaktan baslatildi.",
                    StartIssued = true,
                    RunningConfirmed = true
                };
            }
        }

        var combined = string.Join("\n", new[] { repairOutput, startOutput, queryOutput }
            .Where(o => !string.IsNullOrWhiteSpace(o)));
        var failDiag = ClassifyRemoteOutput(combined);
        return new RemoteServiceStartAttempt
        {
            Status = failDiag.Status == "failed" ? "start-failed" : failDiag.Status,
            Message = failDiag.Message,
            StartIssued = false,
            RunningConfirmed = false
        };
    }

    private static RemoteOutputDiagnosis ClassifyRemoteOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new RemoteOutputDiagnosis("unknown", "Cikti bos — uzak makineye komut gonderilemedi.", true);
        }

        var lower = output.ToLowerInvariant();

        if (lower.Contains("access is denied") ||
            lower.Contains("erisim engellendi") ||
            lower.Contains("erişim engellendi") ||
            lower.Contains("erisim reddedildi") ||
            lower.Contains("erişim reddedildi"))
        {
            return new RemoteOutputDiagnosis(
                "access-denied",
                "Yetki yok (Access Denied) — backend hesabi uzak makinede admin degil.",
                true);
        }

        if (lower.Contains("rpc server is unavailable") ||
            lower.Contains("rpc sunucusu kullanilamiyor") ||
            lower.Contains("rpc sunucusu kullanılamıyor"))
        {
            return new RemoteOutputDiagnosis(
                "rpc-unavailable",
                "RPC sunucusu erisilemez (port 135 ya da SMB/445 kapali olabilir).",
                true);
        }

        if (lower.Contains("the network path was not found") ||
            lower.Contains("ag yolu bulunamadi") ||
            lower.Contains("ağ yolu bulunamadı"))
        {
            return new RemoteOutputDiagnosis(
                "host-unreachable",
                "Ag yolu bulunamadi (SMB/445 erisilemiyor veya host dusmus).",
                true);
        }

        if (lower.Contains("the specified service does not exist") ||
            lower.Contains("1060") ||
            lower.Contains("belirtilen hizmet mevcut degil") ||
            lower.Contains("belirtilen hizmet mevcut değil"))
        {
            return new RemoteOutputDiagnosis(
                "service-missing",
                "Hedef makinede agent servisi yuklu degil (sc 1060).",
                true);
        }

        if (lower.Contains("logon failure") ||
            lower.Contains("oturum acma basarisiz") ||
            lower.Contains("oturum açma başarısız"))
        {
            return new RemoteOutputDiagnosis(
                "auth-failed",
                "Kimlik dogrulama basarisiz — domain/lokal admin kimligi gerekiyor.",
                true);
        }

        if (lower.Contains("the rpc server is too busy"))
        {
            return new RemoteOutputDiagnosis("rpc-busy", "RPC sunucusu mesgul, tekrar denenebilir.", false);
        }

        return new RemoteOutputDiagnosis("failed", ShortenOutput(output), false);
    }

    private sealed record RemoteOutputDiagnosis(string Status, string Message, bool IsHardFailure);

    private async Task<RemoteServiceStartAttempt> TryStartWithScheduledTaskAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var remoteBatPath = $@"\\{ipAddress}\C$\temp\mudo_start_agent_service.bat";
        var localBatPath = Path.Combine(Path.GetTempPath(), $"mudo_start_agent_service_{ipAddress.Replace('.', '_')}.bat");
        var taskName = $"OrchestraStartAgent_{ipAddress.Replace('.', '_')}";
        var remoteLogPath = $@"\\{ipAddress}\C$\temp\mudo_start_agent_service.log";
        var batContent = BuildRemoteServiceRepairBatch();

        await System.IO.File.WriteAllTextAsync(localBatPath, batContent, System.Text.Encoding.ASCII, cancellationToken);

        var scriptPath = Path.Combine(Path.GetTempPath(), $"mudo_start_agent_service_{ipAddress.Replace('.', '_')}.ps1");
        var script = $@"
$ErrorActionPreference = 'Stop'
$ip = '{ipAddress}'
$taskName = '{taskName}'
$batSource = '{localBatPath.Replace("'", "''")}'
$logPath = '{remoteLogPath.Replace("'", "''")}'
try {{
    $remoteTempDir = ""\\$ip\C$\temp""
    if (!(Test-Path $remoteTempDir)) {{ New-Item -Path $remoteTempDir -ItemType Directory -Force | Out-Null }}
    Copy-Item -Path $batSource -Destination ""$remoteTempDir\mudo_start_agent_service.bat"" -Force

    schtasks /Create /S $ip /TN $taskName /TR C:\temp\mudo_start_agent_service.bat /SC ONCE /ST 23:59 /RU SYSTEM /F 2>&1 | Out-Null
    schtasks /Run /S $ip /TN $taskName 2>&1 | Out-Null

    for ($i = 0; $i -lt 5; $i++) {{
        Start-Sleep -Seconds 3
        $svc = sc.exe \\$ip query MudosoftAgentService 2>&1
        if ($svc -match 'RUNNING|START_PENDING') {{
            schtasks /Delete /S $ip /TN $taskName /F 2>&1 | Out-Null
            Write-Output 'STARTED'
            return
        }}
    }}

    if (Test-Path $logPath) {{
        $logTail = (Get-Content $logPath -ErrorAction SilentlyContinue | Select-Object -Last 12) -join ' | '
        if (-not [string]::IsNullOrWhiteSpace($logTail)) {{
            Write-Output $logTail
        }}
    }}

    schtasks /Delete /S $ip /TN $taskName /F 2>&1 | Out-Null
    Write-Output 'NOT_STARTED'
}} catch {{
    try {{ schtasks /Delete /S $ip /TN $taskName /F 2>&1 | Out-Null }} catch {{ }}
    Write-Output ""ERROR: $($_.Exception.Message)""
}}
";

        await System.IO.File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        try
        {
            var (_, output) = await RunPsFileAsync(scriptPath, cancellationToken);
            if (output.Contains("STARTED", StringComparison.OrdinalIgnoreCase))
            {
                return new RemoteServiceStartAttempt
                {
                    Status = "running",
                    Message = "Servis start gorevi gonderildi ve dogrulandi.",
                    StartIssued = true,
                    RunningConfirmed = true
                };
            }

            var fallbackDiag = ClassifyRemoteOutput(output);
            return new RemoteServiceStartAttempt
            {
                Status = fallbackDiag.Status == "failed" ? "start-failed" : fallbackDiag.Status,
                Message = fallbackDiag.Message,
                StartIssued = false,
                RunningConfirmed = false
            };
        }
        finally
        {
            try
            {
                System.IO.File.Delete(localBatPath);
            }
            catch
            {
            }

            try
            {
                System.IO.File.Delete(scriptPath);
            }
            catch
            {
            }

            try
            {
                if (System.IO.File.Exists(remoteBatPath))
                {
                    System.IO.File.Delete(remoteBatPath);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task<string> RunCommandCaptureAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return "Process baslatilamadi.";
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        return $"{output}\n{error}".Trim();
    }

    private static async Task<(int ExitCode, string Output)> RunPsFileAsync(string scriptPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return (-1, "PowerShell baslatilamadi.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        var combined = string.IsNullOrWhiteSpace(error) ? output.Trim() : $"{output.Trim()}\n{error.Trim()}";
        return (process.ExitCode, combined.Trim());
    }

    private static string ShortenOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "Servis baslatilamadi.";
        }

        var normalized = output.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 180 ? normalized : normalized[..180].TrimEnd() + "...";
    }

    private static string BuildRemoteServiceRepairBatch()
    {
        return
            "@echo off\r\n" +
            "setlocal\r\n" +
            "set LOG=C:\\temp\\mudo_start_agent_service.log\r\n" +
            $"set SVC={AgentServiceName}\r\n" +
            $"set EXE={AgentExecutablePath}\r\n" +
            $"> \"%LOG%\" echo [%DATE% %TIME%] Repair start for {AgentServiceName}\r\n" +
            "reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\" /v ServicesPipeTimeout /t REG_DWORD /d 120000 /f >> \"%LOG%\" 2>&1\r\n" +
            "if not exist \"%EXE%\" (\r\n" +
            "  echo Agent executable missing: %EXE%>> \"%LOG%\"\r\n" +
            "  exit /b 2\r\n" +
            ")\r\n" +
            "sc query %SVC% >> \"%LOG%\" 2>&1\r\n" +
            "if errorlevel 1060 (\r\n" +
            $"  sc create %SVC% binPath= \"\\\"%EXE%\\\" --service\" start= delayed-auto obj= LocalSystem DisplayName= \"{AgentServiceDisplayName}\" >> \"%LOG%\" 2>&1\r\n" +
            ") else (\r\n" +
            "  sc config %SVC% binPath= \"\\\"%EXE%\\\" --service\" start= delayed-auto obj= LocalSystem >> \"%LOG%\" 2>&1\r\n" +
            ")\r\n" +
            $"reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\{AgentServiceName}\" /v DelayedAutoStart /t REG_DWORD /d 1 /f >> \"%LOG%\" 2>&1\r\n" +
            "sc description %SVC% \"Orchestra Agent\" >> \"%LOG%\" 2>&1\r\n" +
            "sc failure %SVC% reset= 86400 actions= restart/5000/restart/10000/restart/30000 >> \"%LOG%\" 2>&1\r\n" +
            "net start %SVC% >> \"%LOG%\" 2>&1\r\n" +
            "sc query %SVC% >> \"%LOG%\" 2>&1\r\n";
    }

    private async Task ProcessOfflineServiceStartJobAsync(string jobId, List<Device> offlineDevices)
    {
        if (!_startJobs.TryGetValue(jobId, out var job))
        {
            return;
        }

        try
        {
            foreach (var device in offlineDevices)
            {
                var target = job.Results.FirstOrDefault(r => r.DeviceId == device.Id);
                if (target == null)
                {
                    continue;
                }

                try
                {
                    _logger.LogInformation("Offline servis job hedefi: JobId={JobId} Device={DeviceId} Hostname={Hostname} Ip={IpAddress}", jobId, device.Id, device.Hostname, device.IpAddress);
                    target.Status = "pinging";
                    var pingOk = await TestPingAsync(target.IpAddress);
                    target.PingReachable = pingOk;

                    if (!pingOk)
                    {
                        target.Status = "unreachable";
                        target.Message = "Ping yanit vermedi.";
                        _logger.LogWarning("Offline servis job ping vermedi: JobId={JobId} Device={DeviceId} Ip={IpAddress}", jobId, device.Id, device.IpAddress);
                        continue;
                    }

                    target.Status = "starting";
                    var startResult = await TryStartRemoteAgentServiceAsync(target.IpAddress, CancellationToken.None);
                    target.Status = startResult.Status;
                    target.Message = startResult.Message;
                    target.StartIssued = startResult.StartIssued;
                    target.RunningConfirmed = startResult.RunningConfirmed;
                    _logger.LogInformation(
                        "Offline servis job sonucu: JobId={JobId} Device={DeviceId} Ip={IpAddress} Status={Status} Running={Running}",
                        jobId,
                        device.Id,
                        device.IpAddress,
                        target.Status,
                        target.RunningConfirmed);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Offline servis job hata. JobId={JobId} Device={DeviceId} Ip={IpAddress}", jobId, device.Id, device.IpAddress);
                    target.Status = "error";
                    target.Message = ex.Message;
                }
            }
        }
        finally
        {
            job.CompletedAtUtc = DateTime.UtcNow;
        }
    }

    public sealed class StartOfflineServicesResponse
    {
        public string? JobId { get; init; }
        public int TotalOffline { get; init; }
        public int Attempted { get; init; }
        public int PingReachable { get; init; }
        public int StartIssued { get; init; }
        public int RunningConfirmed { get; init; }
        public string? CompletedAtUtc { get; init; }
        public IReadOnlyList<StartOfflineServiceResult> Results { get; init; } = Array.Empty<StartOfflineServiceResult>();
    }

    public sealed class SetOfflineExclusionRequest
    {
        public bool ExcludeFromOfflineList { get; init; }
    }

    public sealed class SetTemporaryCloseRequest
    {
        public bool IsClosed { get; init; }
        public string? Reason { get; init; }
    }

    public sealed class StartOfflineServiceResult
    {
        public string DeviceId { get; init; } = "";
        public string Hostname { get; init; } = "";
        public string IpAddress { get; init; } = "";
        public int StoreCode { get; init; }
        public bool PingReachable { get; set; }
        public bool StartIssued { get; set; }
        public bool RunningConfirmed { get; set; }
        public string Status { get; set; } = "";
        public string Message { get; set; } = "";
    }

    private sealed class RemoteServiceStartAttempt
    {
        public string Status { get; init; } = "";
        public string Message { get; init; } = "";
        public bool StartIssued { get; init; }
        public bool RunningConfirmed { get; init; }
    }

    private sealed class OfflineServiceStartJob
    {
        public string JobId { get; init; } = "";
        public int TotalOffline { get; init; }
        public DateTime StartedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; set; }
        public List<StartOfflineServiceResult> Results { get; init; } = new();
    }
}
