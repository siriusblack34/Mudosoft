using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Middleware;
using Orchestra.Backend.Services;
using System.IO.Compression;

namespace Orchestra.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/updates")]
public class UpdateController : ControllerBase
{
    private readonly ILogger<UpdateController> _logger;
    private readonly string _updatesPath;
    private readonly string _latestVersionFile;
    private readonly string _historyFile;
    private readonly string _repoRoot;

    // Background build state
    private static bool _isBuilding = false;
    private static string _buildStatus = "";
    private static string _buildError = "";

    public UpdateController(ILogger<UpdateController> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _updatesPath = Path.Combine(env.ContentRootPath, "updates");
        _latestVersionFile = Path.Combine(_updatesPath, "latest.json");
        _historyFile = Path.Combine(_updatesPath, "history.json");
        // Yayınlanan backend: C:\projects\orchestra\backend
        // Repo:               C:\projects\orchestra\repo
        _repoRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "repo"));
        
        // Ensure updates directory exists
        if (!Directory.Exists(_updatesPath))
        {
            Directory.CreateDirectory(_updatesPath);
        }
    }

    /// <summary>
    /// Upload new agent package
    /// POST /api/updates/upload
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100_000_000)] // 100MB limit
    [RequireMenu("/agent-update")]
    public async Task<IActionResult> UploadAgent([FromForm] IFormFile file, [FromForm] string version)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        if (string.IsNullOrWhiteSpace(version))
            return BadRequest("Version is required");

        try
        {
            // Save the zip file
            var fileName = $"MudoSoft.Agent_{version}.zip";
            var filePath = Path.Combine(_updatesPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Update latest.json
            var latestInfo = new
            {
                version = version,
                fileName = fileName,
                uploadedAt = DateTime.UtcNow.ToString("o"),
                sizeBytes = file.Length
            };

            await System.IO.File.WriteAllTextAsync(
                _latestVersionFile,
                System.Text.Json.JsonSerializer.Serialize(latestInfo)
            );

            // Append to history
            var historyList = new List<object>();
            if (System.IO.File.Exists(_historyFile))
            {
                try
                {
                    var existingJson = await System.IO.File.ReadAllTextAsync(_historyFile);
                    var arr = System.Text.Json.JsonDocument.Parse(existingJson).RootElement;
                    foreach (var el in arr.EnumerateArray())
                        historyList.Add(System.Text.Json.JsonSerializer.Deserialize<object>(el.GetRawText())!);
                }
                catch { }
            }
            historyList.Add(new { version, fileName, uploadedAt = DateTime.UtcNow.ToString("o"), sizeBytes = file.Length });
            await System.IO.File.WriteAllTextAsync(
                _historyFile,
                System.Text.Json.JsonSerializer.Serialize(historyList)
            );

            _logger.LogInformation("Agent version {Version} uploaded successfully", version);

            return Ok(new { message = $"Agent {version} uploaded", fileName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload agent");
            return StatusCode(500, "Failed to upload agent");
        }
    }

    /// <summary>
    /// Get latest version info
    /// GET /api/updates/latest
    /// </summary>
    [AllowAnonymous] // Agents need to check for updates without JWT
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatestVersion()
    {
        if (!System.IO.File.Exists(_latestVersionFile))
        {
            return Ok(new { version = "none", message = "No updates available" });
        }

        var json = await System.IO.File.ReadAllTextAsync(_latestVersionFile);
        return Content(json, "application/json");
    }

    /// <summary>
    /// 🔒 Faz 2 (K-3): İmzalı update manifest. Agent bunu pinli backend public key ile doğrular,
    /// indirdiği ZIP'in SHA-256'sını sha256 ile karşılaştırır ve indirmeyi PİNLİ url'den yapar.
    /// İmza = RSA-SHA256(PKCS1) over "{version}|{sha256}|{url}".
    /// </summary>
    [AllowAnonymous]
    [HttpGet("manifest")]
    public async Task<IActionResult> GetManifest([FromServices] Orchestra.Backend.Crypto.RsaKeyProvider rsa)
    {
        if (!System.IO.File.Exists(_latestVersionFile))
            return Ok(new { version = "none" });

        var json = await System.IO.File.ReadAllTextAsync(_latestVersionFile);
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var info = System.Text.Json.JsonSerializer.Deserialize<LatestVersionInfo>(json, options);
        if (info == null || string.IsNullOrEmpty(info.FileName) || string.IsNullOrEmpty(info.Version))
            return NotFound("Invalid version info");

        var filePath = Path.Combine(_updatesPath, info.FileName);
        if (!System.IO.File.Exists(filePath))
            return NotFound("Agent package not found");

        var sha = ComputeSha256Hex(filePath);
        // url RELATİF — agent kendi PİNLİ BackendUrl'ini başa ekler (saldırgan URL veremez).
        var url = $"api/updates/download/{info.Version}";
        var canonical = $"{info.Version}|{sha}|{url}";
        var sig = Convert.ToBase64String(rsa.Sign(System.Text.Encoding.UTF8.GetBytes(canonical)));

        return Ok(new { version = info.Version, sha256 = sha, url, sig });
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long len, DateTime mtime, string sha)> _shaCache = new();
    private static string ComputeSha256Hex(string filePath)
    {
        var fi = new FileInfo(filePath);
        if (_shaCache.TryGetValue(filePath, out var c) && c.len == fi.Length && c.mtime == fi.LastWriteTimeUtc)
            return c.sha;
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = System.IO.File.OpenRead(filePath);
        var hex = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        _shaCache[filePath] = (fi.Length, fi.LastWriteTimeUtc, hex);
        return hex;
    }

    /// <summary>
    /// Download latest agent package
    /// GET /api/updates/download
    /// </summary>
    [AllowAnonymous] // Agents download updates without JWT
    [HttpGet("download")]
    public async Task<IActionResult> DownloadLatest()
    {
        if (!System.IO.File.Exists(_latestVersionFile))
        {
            return NotFound("No updates available");
        }

        var json = await System.IO.File.ReadAllTextAsync(_latestVersionFile);
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var info = System.Text.Json.JsonSerializer.Deserialize<LatestVersionInfo>(json, options);
        
        if (info == null || string.IsNullOrEmpty(info.FileName))
        {
            return NotFound($"Invalid version info. FileName is null. JSON: {json}");
        }

        var filePath = Path.Combine(_updatesPath, info.FileName);
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("Agent package not found");
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "application/zip", info.FileName);
    }

    /// <summary>
    /// TightVNC MSI indirme — mağaza agent'ı (VncInstallerService) VNC kurulumu sırasında buradan çeker.
    /// Dosya: {ContentRoot}/central-agent/tightvnc.msi, yoksa {ContentRoot}/updates/tightvnc.msi.
    /// </summary>
    [AllowAnonymous] // Agent JWT'siz indirir (diğer download uçları gibi)
    [HttpGet("vnc-installer")]
    public IActionResult DownloadVncInstaller()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(_updatesPath, "..", "central-agent", "tightvnc.msi")),
            Path.Combine(_updatesPath, "tightvnc.msi"),
        };
        var path = candidates.FirstOrDefault(System.IO.File.Exists);
        if (path == null)
        {
            _logger.LogWarning("[VNC] vnc-installer istendi ama tightvnc.msi sunucuda bulunamadı");
            return NotFound(new { error = "tightvnc.msi sunucuda bulunamadı." });
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/octet-stream", "tightvnc.msi");
    }

    /// <summary>
    /// Download specific version
    /// GET /api/updates/download/{version}
    /// </summary>
    [AllowAnonymous] // Agents download updates without JWT
    [HttpGet("download/{version}")]
    public async Task<IActionResult> DownloadVersion(string version)
    {
        var fileName = $"MudoSoft.Agent_{version}.zip";
        var filePath = Path.Combine(_updatesPath, fileName);
        
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound($"Version {version} not found");
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "application/zip", fileName);
    }

    /// <summary>
    /// Trigger update on a specific device
    /// POST /api/updates/trigger
    /// </summary>
    [HttpPost("trigger")]
    [RequireMenu("/agent-update")]
    public IActionResult TriggerUpdate(
        [FromQuery] string deviceId,
        [FromQuery] string? backendUrl,
        [FromServices] Orchestra.Backend.Data.CommandQueue queue)
    {
        if (string.IsNullOrEmpty(deviceId))
            return BadRequest("deviceId required");

        var url = !string.IsNullOrEmpty(backendUrl)
            ? backendUrl
            : $"{Request.Scheme}://{Request.Host}";

        var commandId = Guid.NewGuid();
        var updatePayload = System.Text.Json.JsonSerializer.Serialize(new { backendUrl = url.TrimEnd('/') });

        queue.Enqueue(new Orchestra.Shared.Dtos.CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = Orchestra.Shared.Enums.CommandType.UpdateAgent,
            Payload = updatePayload,
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("Update command queued for device {DeviceId}", deviceId);

        return Ok(new { commandId, message = "Update command queued" });
    }

    /// <summary>
    /// Serves a batch updater script that agents download and execute
    /// GET /api/updates/updater-cmd?backendUrl=...
    /// </summary>
    [AllowAnonymous] // Agents download updater script without JWT
    [HttpGet("updater-cmd")]
    public IActionResult GetUpdaterCmd([FromQuery] string? backendUrl)
    {
        var url = !string.IsNullOrEmpty(backendUrl)
            ? backendUrl.TrimEnd('/')
            : $"{Request.Scheme}://{Request.Host}";

        var batch = $@"@echo off
set LOG=C:\Users\Public\MudoSoftUpdate\update.log
set UPDATEDIR=C:\Users\Public\MudoSoftUpdate
set ZIPFILE=C:\Users\Public\MudoSoftUpdate\agent.zip
set EXTRACTDIR=C:\Users\Public\MudoSoftUpdate\extracted
set INSTALLDIR=C:\Program Files\MudoSoft\Agent
set SERVICENAME=MudosoftAgentService

echo [%date% %time%] === Orchestra Agent Updater v3 === > %LOG%

echo [%date% %time%] Step 1: Stopping service... >> %LOG%
net stop %SERVICENAME% >> %LOG% 2>&1
timeout /t 3 /nobreak > nul

echo [%date% %time%] Step 2: Killing processes... >> %LOG%
taskkill /F /IM MudoSoft.Tray.exe 2>nul
taskkill /F /IM MudoSoft.Agent.exe 2>nul
timeout /t 5 /nobreak > nul

echo [%date% %time%] Step 3: Cleaning old files... >> %LOG%
if exist %EXTRACTDIR% rmdir /S /Q %EXTRACTDIR%
if exist %ZIPFILE% del /F /Q %ZIPFILE%

echo [%date% %time%] Step 4: Downloading ZIP... >> %LOG%
powershell -NoProfile -Command ""(New-Object Net.WebClient).DownloadFile('{url}/api/updates/download','C:\Users\Public\MudoSoftUpdate\agent.zip')"" >> %LOG% 2>&1

if not exist %ZIPFILE% (
    echo [%date% %time%] DOWNLOAD FAILED >> %LOG%
    net start %SERVICENAME% >> %LOG% 2>&1
    exit /b 1
)

echo [%date% %time%] Step 5: Extracting... >> %LOG%
mkdir %EXTRACTDIR% 2>nul

REM Try Expand-Archive (PS 5+, W10/W11), fallback to Shell.Application COM (PS 2+, W7)
powershell -NoProfile -Command ""if (Get-Command Expand-Archive -ErrorAction SilentlyContinue) {{ Expand-Archive -Path 'C:\Users\Public\MudoSoftUpdate\agent.zip' -DestinationPath 'C:\Users\Public\MudoSoftUpdate\extracted' -Force }} else {{ $s = New-Object -ComObject Shell.Application; $z = $s.NameSpace('C:\Users\Public\MudoSoftUpdate\agent.zip'); $d = $s.NameSpace('C:\Users\Public\MudoSoftUpdate\extracted'); $d.CopyHere($z.Items(), 256) }}"" >> %LOG% 2>&1
timeout /t 3 /nobreak > nul

REM Verify extraction
dir %EXTRACTDIR%\*.exe >nul 2>&1
if %errorlevel% neq 0 (
    echo [%date% %time%] EXTRACT FAILED - no exe found >> %LOG%
    net start %SERVICENAME% >> %LOG% 2>&1
    exit /b 1
)
echo [%date% %time%] Extraction OK >> %LOG%

REM Preserve existing appsettings.json (contains machine-specific StoreCode)
echo [%date% %time%] Step 6: Preserving config and copying files... >> %LOG%
if exist %EXTRACTDIR%\appsettings.json del /F /Q %EXTRACTDIR%\appsettings.json
if exist %EXTRACTDIR%\appsettings.Development.json del /F /Q %EXTRACTDIR%\appsettings.Development.json
xcopy %EXTRACTDIR%\* ""%INSTALLDIR%\"" /E /Y /Q >> %LOG% 2>&1

echo [%date% %time%] Step 7: Starting service... >> %LOG%
net start %SERVICENAME% >> %LOG% 2>&1
if %errorlevel% neq 0 (
    timeout /t 5 /nobreak > nul
    net start %SERVICENAME% >> %LOG% 2>&1
)

sc query %SERVICENAME% | findstr RUNNING >> %LOG% 2>&1
if %errorlevel% equ 0 (
    echo [%date% %time%] Service running OK >> %LOG%
) else (
    echo [%date% %time%] WARNING: Service NOT running! >> %LOG%
)

echo [%date% %time%] === UPDATE COMPLETE === >> %LOG%
";
        return Content(batch, "text/plain");
    }

    /// <summary>
    /// Force update on a specific device
    /// POST /api/updates/force-update
    /// </summary>
    [HttpPost("force-update")]
    [RequireMenu("/agent-update")]
    public IActionResult ForceUpdate(
        [FromQuery] string deviceId,
        [FromQuery] string? backendUrl,
        [FromServices] Orchestra.Backend.Data.CommandQueue queue)
    {
        if (string.IsNullOrEmpty(deviceId))
            return BadRequest("deviceId required");

        var url = !string.IsNullOrEmpty(backendUrl)
            ? backendUrl.TrimEnd('/')
            : $"{Request.Scheme}://{Request.Host}";

        var commandId = Guid.NewGuid();
        var updatePayload = System.Text.Json.JsonSerializer.Serialize(new { backendUrl = url.TrimEnd('/') });

        queue.Enqueue(new Orchestra.Shared.Dtos.CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = Orchestra.Shared.Enums.CommandType.UpdateAgent,
            Payload = updatePayload,
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("Force update queued for device {DeviceId}", deviceId);
        return Ok(new { commandId, message = $"Force update queued for {deviceId}" });
    }

    /// <summary>
    /// Trigger update on all online devices
    /// POST /api/updates/trigger-all
    /// </summary>
    [HttpPost("trigger-all")]
    [RequireMenu("/agent-update")]
    public async Task<IActionResult> TriggerUpdateAll(
        [FromQuery] string? backendUrl,
        [FromServices] Orchestra.Backend.Data.CommandQueue queue, 
        [FromServices] Orchestra.Backend.Data.OrchestraDbContext dbContext)
    {
        var url = !string.IsNullOrEmpty(backendUrl)
            ? backendUrl
            : $"{Request.Scheme}://{Request.Host}";
        var updatePayload = System.Text.Json.JsonSerializer.Serialize(new { backendUrl = url.TrimEnd('/') });

        // Son 5 dakika içinde heartbeat göndermiş TÜM cihazlara gönder
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var targetDevices = await dbContext.Devices
            .Where(d => d.LastSeen != null && d.LastSeen > cutoff)
            .Select(d => new { d.Id, d.Hostname, d.AgentVersion })
            .ToListAsync();

        foreach (var device in targetDevices)
        {
            queue.Enqueue(new Orchestra.Shared.Dtos.CommandDto
            {
                Id = Guid.NewGuid(),
                DeviceId = device.Id,
                Type = Orchestra.Shared.Enums.CommandType.UpdateAgent,
                Payload = updatePayload,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        _logger.LogInformation("Update command queued for {Count} devices: {Devices}",
            targetDevices.Count,
            string.Join(", ", targetDevices.Select(d => $"{d.Hostname}({d.AgentVersion})")));

        return Ok(new { count = targetDevices.Count, message = $"Update queued for {targetDevices.Count} devices" });
    }

    /// <summary>
    /// Get version history
    /// GET /api/updates/history
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        if (!System.IO.File.Exists(_historyFile))
            return Ok(new List<object>());

        var json = await System.IO.File.ReadAllTextAsync(_historyFile);
        return Content(json, "application/json");
    }

    /// <summary>
    /// Get all devices with their current agent version
    /// GET /api/updates/device-versions
    /// </summary>
    [HttpGet("device-versions")]
    public async Task<IActionResult> GetDeviceVersions(
        [FromServices] Orchestra.Backend.Data.OrchestraDbContext dbContext)
    {
        var devices = await dbContext.Devices
            .Select(d => new
            {
                d.Id,
                d.Hostname,
                d.Online,
                d.AgentVersion,
                d.StoreCode,
                d.LastSeen
            })
            .OrderBy(d => d.Hostname)
            .ToListAsync();

        return Ok(devices);
    }

    /// <summary>
    /// Start an automated build process
    /// POST /api/updates/build
    /// </summary>
    [HttpPost("build")]
    [RequireMenu("/agent-update")]
    public IActionResult StartBuild()
    {
        if (_isBuilding) return BadRequest("Build already in progress");

        var projectRoot = _repoRoot;
        var csprojPath = Path.Combine(projectRoot, "agent", "Orchestra.Agent.csproj");

        if (!System.IO.File.Exists(csprojPath))
            return BadRequest($"Agent projesi bulunamadı: {csprojPath}  (repo: {_repoRoot})");

        _isBuilding = true;
        _buildStatus = "Hazırlanıyor...";
        _buildError = "";

        Task.Run(async () =>
        {
            var outputDir = Path.Combine(Path.GetTempPath(), "OrchestraAgentBuild");
            try
            {
                // 1. Read and increment version
                var csprojContent = await System.IO.File.ReadAllTextAsync(csprojPath);
                var versionRegex = new System.Text.RegularExpressions.Regex(@"<Version>(.*?)</Version>");
                var match = versionRegex.Match(csprojContent);
                if (!match.Success) throw new Exception("Version tag not found in csproj");

                var currentVersion = match.Groups[1].Value;
                var parts = currentVersion.Split('.');
                parts[parts.Length - 1] = (int.Parse(parts.Last()) + 1).ToString();
                var newVersion = string.Join(".", parts);

                // Update all version tags
                csprojContent = csprojContent
                    .Replace($"<Version>{currentVersion}</Version>", $"<Version>{newVersion}</Version>")
                    .Replace($"<AssemblyVersion>{currentVersion}</AssemblyVersion>", $"<AssemblyVersion>{newVersion}</AssemblyVersion>")
                    .Replace($"<FileVersion>{currentVersion}</FileVersion>", $"<FileVersion>{newVersion}</FileVersion>");
                await System.IO.File.WriteAllTextAsync(csprojPath, csprojContent);

                _buildStatus = $"v{newVersion} — Agent derleniyor (self-contained)...";
                _logger.LogInformation("Build started: v{Version}", newVersion);

                // 2. Clean output directory
                if (Directory.Exists(outputDir))
                    Directory.Delete(outputDir, true);
                Directory.CreateDirectory(outputDir);

                // 3. Publish Agent (self-contained)
                await RunDotnetPublish(Path.Combine(projectRoot, "agent"), outputDir);
                _buildStatus = $"v{newVersion} — Tray derleniyor...";

                // 5. Publish Tray (self-contained)
                await RunDotnetPublish(Path.Combine(projectRoot, "tray"), outputDir);
                _buildStatus = $"v{newVersion} — ZIP oluşturuluyor...";

                // 5.5. Remove appsettings from output to prevent overwriting machine-specific config
                var appSettingsInOutput = Path.Combine(outputDir, "appsettings.json");
                var appSettingsDevInOutput = Path.Combine(outputDir, "appsettings.Development.json");
                if (System.IO.File.Exists(appSettingsInOutput))
                    System.IO.File.Delete(appSettingsInOutput);
                if (System.IO.File.Exists(appSettingsDevInOutput))
                    System.IO.File.Delete(appSettingsDevInOutput);

                // 6. Create ZIP
                var destFileName = $"MudoSoft.Agent_{newVersion}.zip";
                var destPath = Path.Combine(_updatesPath, destFileName);
                
                if (System.IO.File.Exists(destPath))
                    System.IO.File.Delete(destPath);
                    
                System.IO.Compression.ZipFile.CreateFromDirectory(outputDir, destPath);

                var fileInfo = new System.IO.FileInfo(destPath);
                var sizeMB = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 1);
                _buildStatus = $"v{newVersion} — Kaydediliyor ({sizeMB} MB)...";

                // 7. Update latest.json
                var latestInfo = new
                {
                    version = newVersion,
                    fileName = destFileName,
                    uploadedAt = DateTime.UtcNow.ToString("o"),
                    sizeBytes = fileInfo.Length
                };

                await System.IO.File.WriteAllTextAsync(
                    _latestVersionFile,
                    System.Text.Json.JsonSerializer.Serialize(latestInfo)
                );

                // 8. Update history.json
                var historyList = new List<object>();
                if (System.IO.File.Exists(_historyFile))
                {
                    try
                    {
                        var existingJson = await System.IO.File.ReadAllTextAsync(_historyFile);
                        var arr = System.Text.Json.JsonDocument.Parse(existingJson).RootElement;
                        foreach (var el in arr.EnumerateArray())
                            historyList.Add(System.Text.Json.JsonSerializer.Deserialize<object>(el.GetRawText())!);
                    }
                    catch { }
                }
                historyList.Add(new { version = newVersion, fileName = destFileName, uploadedAt = latestInfo.uploadedAt, sizeBytes = fileInfo.Length });
                await System.IO.File.WriteAllTextAsync(
                    _historyFile,
                    System.Text.Json.JsonSerializer.Serialize(historyList)
                );

                _buildStatus = $"✅ v{newVersion} build tamamlandı! ({sizeMB} MB)";
                _logger.LogInformation("Build completed: v{Version} ({SizeMB} MB)", newVersion, sizeMB);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Build failed");
                _buildStatus = "❌ Hata: " + ex.Message;
                _buildError = ex.ToString();
            }
            finally
            {
                _isBuilding = false;
            }
        });

        return Accepted(new { message = "Build started" });
    }

    /// <summary>
    /// Run dotnet publish for a project (self-contained, win-x64)
    /// </summary>
    private async Task RunDotnetPublish(string projectPath, string outputDir)
    {
        var projectFile = Path.GetFileName(projectPath);
        var projectDir = Path.GetDirectoryName(projectPath);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{projectFile}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o \"{outputDir}\"",
            WorkingDirectory = projectDir,  // Set working directory to project folder
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null) throw new Exception($"Failed to start dotnet publish for {projectFile}");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var errorMsg = $"{projectFile} publish failed (Exit: {process.ExitCode})";
            if (!string.IsNullOrEmpty(stderr))
                errorMsg += $"\n{stderr}";
            if (!string.IsNullOrEmpty(stdout))
                errorMsg += $"\n{stdout}";
            throw new Exception(errorMsg);
        }
    }

    /// <summary>
    /// Get background build status
    /// GET /api/updates/build-status
    /// </summary>
    [HttpGet("build-status")]
    public IActionResult GetBuildStatus()
    {
        return Ok(new
        {
            isBuilding = _isBuilding,
            status = _buildStatus,
            error = _buildError
        });
    }

    /// <summary>
    /// Builds a simple PowerShell one-liner that downloads updater.cmd and launches it detached.
    /// Uses cmd.exe /C start to ensure updater runs independently of agent process (Session 0 compatible).
    /// </summary>
    private string BuildUpdateScript(string backendUrl)
    {
        var url = backendUrl.TrimEnd('/');
        var downloadUrl = url + "/api/updates/updater-cmd?backendUrl=" + url;

        // Download updater.cmd then launch via WMI - creates a fully detached process
        // that survives agent shutdown on W11 (unlike Start-Process which dies with parent)
        return "if (!(Test-Path 'C:\\Users\\Public\\MudoSoftUpdate')) { $null = New-Item 'C:\\Users\\Public\\MudoSoftUpdate' -ItemType Directory }; "
             + "$wc = New-Object System.Net.WebClient; "
             + "$wc.DownloadFile('" + downloadUrl + "','C:\\Users\\Public\\MudoSoftUpdate\\updater.cmd'); "
             + "wmic process call create 'cmd.exe /C C:\\Users\\Public\\MudoSoftUpdate\\updater.cmd'";
    }

    private class LatestVersionInfo
    {
        public string? Version { get; set; }
        public string? FileName { get; set; }
        public string? UploadedAt { get; set; }
        public long SizeBytes { get; set; }
    }
}
