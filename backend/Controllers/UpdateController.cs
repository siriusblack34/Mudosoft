using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Services;
using System.IO.Compression;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/updates")]
public class UpdateController : ControllerBase
{
    private readonly ILogger<UpdateController> _logger;
    private readonly string _updatesPath;
    private readonly string _latestVersionFile;
    private readonly string _historyFile;

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
    /// Download latest agent package
    /// GET /api/updates/download
    /// </summary>
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
    /// Download specific version
    /// GET /api/updates/download/{version}
    /// </summary>
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
    public IActionResult TriggerUpdate(
        [FromQuery] string deviceId, 
        [FromQuery] string? backendUrl,
        [FromServices] MudoSoft.Backend.Data.CommandQueue queue)
    {
        if (string.IsNullOrEmpty(deviceId))
            return BadRequest("deviceId required");

        // Use provided backendUrl or fallback to Request.Host
        var url = !string.IsNullOrEmpty(backendUrl) 
            ? backendUrl 
            : $"{Request.Scheme}://{Request.Host}";

        var commandId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            backendUrl = url
        });

        queue.Enqueue(new Mudosoft.Shared.Dtos.CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = Mudosoft.Shared.Enums.CommandType.UpdateAgent,
            Payload = payload,
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("Update command queued for device {DeviceId}", deviceId);

        return Ok(new { commandId, message = "Update command queued" });
    }

    /// <summary>
    /// Serves a batch updater script that agents download and execute
    /// GET /api/updates/updater-cmd?backendUrl=...
    /// </summary>
    [HttpGet("updater-cmd")]
    public IActionResult GetUpdaterCmd([FromQuery] string? backendUrl)
    {
        var url = !string.IsNullOrEmpty(backendUrl)
            ? backendUrl.TrimEnd('/')
            : $"{Request.Scheme}://{Request.Host}";

        var batch = $@"@echo off
set LOG=""C:\Users\Public\MudoSoftUpdate\update.log""
echo [%date% %time%] === MudoSoft Agent Updater === > %LOG%

echo [%date% %time%] Step 1: Stopping service... >> %LOG%
net stop MudosoftAgentService >> %LOG% 2>&1

echo [%date% %time%] Step 2: Killing ALL MudoSoft processes... >> %LOG%
taskkill /F /IM MudoSoft.Tray.exe 2>nul
taskkill /F /IM MudoSoft.Agent.exe 2>nul
timeout /t 10 /nobreak > nul

echo [%date% %time%] Step 3: Cleaning old update files... >> %LOG%
if exist ""C:\Users\Public\MudoSoftUpdate\extracted"" rmdir /S /Q ""C:\Users\Public\MudoSoftUpdate\extracted""
if exist ""C:\Users\Public\MudoSoftUpdate\agent.zip"" del /F /Q ""C:\Users\Public\MudoSoftUpdate\agent.zip""

echo [%date% %time%] Step 4: Downloading ZIP... >> %LOG%
powershell -Command ""(New-Object Net.WebClient).DownloadFile('{url}/api/updates/download','C:\Users\Public\MudoSoftUpdate\agent.zip')"" >> %LOG% 2>&1

if not exist ""C:\Users\Public\MudoSoftUpdate\agent.zip"" (
    echo [%date% %time%] DOWNLOAD FAILED - restarting service >> %LOG%
    net start MudosoftAgentService >> %LOG% 2>&1
    exit /b 1
)

echo [%date% %time%] Step 5: Extracting... >> %LOG%
mkdir ""C:\Users\Public\MudoSoftUpdate\extracted"" 2>nul
echo Set objShell = CreateObject(""Shell.Application"") > ""C:\Users\Public\MudoSoftUpdate\unzip.vbs""
echo Set objSource = objShell.NameSpace(""C:\Users\Public\MudoSoftUpdate\agent.zip"") >> ""C:\Users\Public\MudoSoftUpdate\unzip.vbs""
echo Set objTarget = objShell.NameSpace(""C:\Users\Public\MudoSoftUpdate\extracted"") >> ""C:\Users\Public\MudoSoftUpdate\unzip.vbs""
echo objTarget.CopyHere objSource.Items, 256 >> ""C:\Users\Public\MudoSoftUpdate\unzip.vbs""
cscript //nologo ""C:\Users\Public\MudoSoftUpdate\unzip.vbs"" >> %LOG% 2>&1

echo [%date% %time%] Step 6: Copying files... >> %LOG%
xcopy ""C:\Users\Public\MudoSoftUpdate\extracted\*"" ""C:\Program Files\MudoSoft\Agent\"" /E /Y /Q >> %LOG% 2>&1

echo [%date% %time%] Step 7: Starting service... >> %LOG%
net start MudosoftAgentService >> %LOG% 2>&1

echo [%date% %time%] === UPDATE COMPLETE === >> %LOG%
";
        return Content(batch, "text/plain");
    }

    /// <summary>
    /// Force update using a simple one-liner that downloads and runs updater.cmd
    /// POST /api/updates/force-update
    /// </summary>
    [HttpPost("force-update")]
    public IActionResult ForceUpdate(
        [FromQuery] string deviceId,
        [FromQuery] string? backendUrl,
        [FromServices] MudoSoft.Backend.Data.CommandQueue queue)
    {
        if (string.IsNullOrEmpty(deviceId))
            return BadRequest("deviceId required");

        var url = !string.IsNullOrEmpty(backendUrl)
            ? backendUrl.TrimEnd('/')
            : $"{Request.Scheme}://{Request.Host}";

        // Pure PowerShell - PS 2.0 compatible, no & or complex quoting
        // Uses ; separator and simple cmdlets only
        var downloadUrl = url + "/api/updates/updater-cmd?backendUrl=" + url;
        var script = "if (!(Test-Path 'C:\\Users\\Public\\MudoSoftUpdate')) { $null = New-Item 'C:\\Users\\Public\\MudoSoftUpdate' -ItemType Directory }; $wc = New-Object System.Net.WebClient; $wc.DownloadFile('" + downloadUrl + "','C:\\Users\\Public\\MudoSoftUpdate\\updater.cmd'); Start-Process 'C:\\Users\\Public\\MudoSoftUpdate\\updater.cmd'";

        var commandId = Guid.NewGuid();
        queue.Enqueue(new Mudosoft.Shared.Dtos.CommandDto
        {
            Id = commandId,
            DeviceId = deviceId,
            Type = Mudosoft.Shared.Enums.CommandType.ExecuteScript,
            Payload = script,
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
    public async Task<IActionResult> TriggerUpdateAll(
        [FromQuery] string? backendUrl,
        [FromServices] MudoSoft.Backend.Data.CommandQueue queue, 
        [FromServices] MudoSoft.Backend.Data.MudoSoftDbContext dbContext)
    {
        // Use provided backendUrl or fallback to Request.Host
        var url = !string.IsNullOrEmpty(backendUrl) 
            ? backendUrl 
            : $"{Request.Scheme}://{Request.Host}";
        var payload = System.Text.Json.JsonSerializer.Serialize(new { backendUrl = url });

        var onlineDevices = await dbContext.Devices
            .Where(d => d.Online && !string.IsNullOrEmpty(d.AgentVersion))
            .Select(d => d.Id)
            .ToListAsync();

        foreach (var deviceId in onlineDevices)
        {
            queue.Enqueue(new Mudosoft.Shared.Dtos.CommandDto
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                Type = Mudosoft.Shared.Enums.CommandType.UpdateAgent,
                Payload = payload,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        _logger.LogInformation("Update command queued for {Count} devices", onlineDevices.Count);

        return Ok(new { count = onlineDevices.Count, message = $"Update queued for {onlineDevices.Count} devices" });
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
        [FromServices] MudoSoft.Backend.Data.MudoSoftDbContext dbContext)
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
    public IActionResult StartBuild()
    {
        if (_isBuilding) return BadRequest("Build already in progress");

        var projectRoot = @"c:\Projects\mudosoft";
        var csprojPath = Path.Combine(projectRoot, "agent", "MudoSoft.Agent.csproj");

        if (!System.IO.File.Exists(csprojPath))
            return BadRequest($"Agent projesi bulunamadı: {csprojPath}");

        _isBuilding = true;
        _buildStatus = "Hazırlanıyor...";
        _buildError = "";

        Task.Run(async () =>
        {
            var outputDir = @"E:\AgentDeploy";
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
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{projectPath}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o \"{outputDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null) throw new Exception($"Failed to start dotnet publish for {projectPath}");

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"dotnet publish failed for {Path.GetFileName(projectPath)}. Exit: {process.ExitCode}. Error: {stderr}");
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

    private class LatestVersionInfo
    {
        public string? Version { get; set; }
        public string? FileName { get; set; }
        public string? UploadedAt { get; set; }
        public long SizeBytes { get; set; }
    }
}
