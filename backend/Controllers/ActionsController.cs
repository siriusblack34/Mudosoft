using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mudosoft.Shared.Dtos;
using Mudosoft.Shared.Enums;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using System.Text;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ActionsController : ControllerBase
{
    private readonly CommandQueue _queue;
    private readonly ILogger<ActionsController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly MudoSoftDbContext _dbContext;
    private readonly Dictionary<string, string> _batchTemplates;
    private const string ServiceInventoryScript = """
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Web.Extensions
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $serializer.MaxJsonLength = 67108864

    $services = New-Object System.Collections.ArrayList

    Get-WmiObject Win32_Service | ForEach-Object {
        $item = New-Object 'System.Collections.Generic.Dictionary[string,string]'
        $item.Add('Name', [string]$_.Name)
        $item.Add('DisplayName', [string]$_.DisplayName)
        $item.Add('Status', [string]$_.State)
        $item.Add('StartType', [string]$_.StartMode)
        [void]$services.Add($item)
    }

    $json = $serializer.Serialize($services)
    Write-Output "__MUDOSOFT_JSON_BEGIN__"
    Write-Output $json
    Write-Output "__MUDOSOFT_JSON_END__"
}
catch {
    Write-Output "__MUDOSOFT_ERROR__"
    Write-Output $_.Exception.Message
    exit 1
}
""";

    public ActionsController(
        CommandQueue queue,
        ILogger<ActionsController> logger,
        IWebHostEnvironment env,
        MudoSoftDbContext dbContext)
    {
        _queue = queue;
        _logger = logger;
        _env = env;
        _dbContext = dbContext;

        var repoRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, ".."));
        _batchTemplates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["uninstall-agent"] = Path.Combine(repoRoot, "uninstall_agent.bat"),
            ["deep-clean-uninstall"] = Path.Combine(repoRoot, "installer", "uninstall_mudosoft_and_vnc_deep_clean.bat"),
            ["fix-admin-share"] = Path.Combine(repoRoot, "fix_admin_share.bat")
        };
    }

    /// <summary>
    /// Belirtilen cihazı yeniden başlatma komutunu kuyruğa alır.
    /// </summary>
    [HttpPost("reboot")]
    public IActionResult Reboot([FromBody] ExecuteActionRequest request)
    {
        _queue.Enqueue(new CommandDto
        {
            Id = Guid.NewGuid(),
            DeviceId = request.DeviceId,
            Type = CommandType.Reboot,
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("Reboot komutu kuyruğa alındı: {DeviceId}", request.DeviceId);
        return Accepted();
    }

    /// <summary>
    /// Belirtilen cihazda script çalıştırma komutunu kuyruğa alır. (YENİ)
    /// </summary>
    [HttpPost("run-script")]
    public IActionResult RunScript([FromBody] ExecuteActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Script))
        {
            return BadRequest("Script içeriği boş olamaz.");
        }

        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = request.DeviceId,
            Type = CommandType.ExecuteScript, // Yeni komut tipi
            Payload = request.Script,        // Çalıştırılacak script
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("Script çalıştırma komutu kuyruğa alındı: {DeviceId}, CommandId: {CommandId}", 
                               request.DeviceId, commandId);
        
        // Komutun benzersiz kimliğini döndürerek Frontend'in sonucu takip etmesini sağlarız.
        return Accepted(new { commandId = commandId.ToString() }); 
    }

    /// <summary>
    /// Belirtilen cihazın servis envanterini listeleme komutunu kuyruğa alır.
    /// </summary>
    [HttpPost("list-services")]
    public IActionResult ListServices([FromBody] DeviceActionRequest request)
    {
        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = request.DeviceId,
            Type = CommandType.ExecuteScript,
            Payload = ServiceInventoryScript,
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("ListServices script komutu kuyruğa alındı: {DeviceId}, CommandId: {CommandId}",
            request.DeviceId, commandId);

        return Accepted(new { commandId = commandId.ToString() });
    }

    /// <summary>
    /// Belirtilen cihazda klasör içeriğini temizleme komutunu kuyruğa alır.
    /// </summary>
    [HttpPost("folder-cleanup")]
    public IActionResult FolderCleanup([FromBody] FolderCleanupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return BadRequest("Path boş olamaz.");
        }

        var commandId = Guid.NewGuid();
        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = request.DeviceId,
            Type = CommandType.FolderCleanup,
            Payload = request.Path,
            CreatedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("FolderCleanup komutu kuyruğa alındı: {DeviceId}, Path: {Path}, CommandId: {CommandId}", 
                               request.DeviceId, request.Path, commandId);
        
        return Accepted(new { commandId = commandId.ToString() }); 
    }

    /// <summary>
    /// Repo içindeki izinli batch şablonlarından birini seçili cihazlara kuyruklar.
    /// Amaç: RDP/Dameware açmadan agent üzerinden toplu işlem çalıştırmak.
    /// </summary>
    [HttpPost("run-batch-template")]
    public async Task<IActionResult> RunBatchTemplate([FromBody] BatchTemplateRunRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Template))
            return BadRequest("Template boş olamaz.");

        if (!_batchTemplates.TryGetValue(request.Template, out var batchPath))
            return BadRequest(new
            {
                error = "Bilinmeyen template.",
                availableTemplates = _batchTemplates.Keys.OrderBy(x => x).ToArray()
            });

        if (!System.IO.File.Exists(batchPath))
            return NotFound($"Template dosyası bulunamadı: {batchPath}");

        var normalizedDeviceIds = (request.DeviceIds ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var normalizedStoreCodes = (request.StoreCodes ?? new List<int>())
            .Distinct()
            .ToList();

        if (normalizedDeviceIds.Count == 0 && normalizedStoreCodes.Count == 0 && !request.SelectAllMatching)
            return BadRequest("En az bir deviceId veya storeCode gönderilmelidir.");

        var devicesQuery = _dbContext.Devices.AsNoTracking().AsQueryable();

        if (request.OnlineOnly)
            devicesQuery = devicesQuery.Where(d => d.Online);

        if (!string.IsNullOrWhiteSpace(request.OsContains))
        {
            var osFilter = request.OsContains.Trim();
            devicesQuery = devicesQuery.Where(d => d.Os != null && EF.Functions.ILike(d.Os, $"%{osFilter}%"));
        }

        if (normalizedDeviceIds.Count > 0 || normalizedStoreCodes.Count > 0)
        {
            devicesQuery = devicesQuery.Where(d =>
                normalizedDeviceIds.Contains(d.Id) ||
                normalizedStoreCodes.Contains(d.StoreCode));
        }

        var devices = await devicesQuery
            .OrderBy(d => d.StoreCode)
            .ThenBy(d => d.Hostname)
            .ToListAsync(ct);

        if (devices.Count == 0)
        {
            return NotFound(new
            {
                error = request.OnlineOnly
                    ? "Eşleşen online cihaz bulunamadı."
                    : "Eşleşen cihaz bulunamadı."
            });
        }

        var batchContent = await System.IO.File.ReadAllTextAsync(batchPath, Encoding.ASCII, ct);
        var wrappedScript = BuildBatchExecutionScript(batchContent, Path.GetFileName(batchPath));

        var queued = new List<object>();
        foreach (var device in devices)
        {
            var commandId = Guid.NewGuid();
            _queue.Enqueue(new CommandDto
            {
                Id = commandId,
                DeviceId = device.Id,
                Type = CommandType.ExecuteScript,
                Payload = wrappedScript,
                CreatedAtUtc = DateTime.UtcNow
            });

            queued.Add(new
            {
                commandId,
                deviceId = device.Id,
                hostname = device.Hostname,
                ipAddress = device.IpAddress,
                storeCode = device.StoreCode
            });
        }

        _logger.LogInformation(
            "Batch template queued. Template={Template} DeviceCount={DeviceCount} OnlineOnly={OnlineOnly}",
            request.Template,
            queued.Count,
            request.OnlineOnly);

        return Accepted(new
        {
            template = request.Template,
            sourceFile = Path.GetFileName(batchPath),
            queuedCount = queued.Count,
            queued
        });
    }

    [HttpGet("batch-templates")]
    public IActionResult GetBatchTemplates()
    {
        return Ok(_batchTemplates.Keys
            .OrderBy(x => x)
            .Select(key => new { key, fileName = Path.GetFileName(_batchTemplates[key]) }));
    }

    private static string BuildBatchExecutionScript(string batchContent, string fileName)
    {
        var base64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(batchContent));

        return $$"""
$ErrorActionPreference = 'Stop'
$batchName = '{{fileName}}'
$batchPath = Join-Path $env:TEMP ("mudosoft_" + [guid]::NewGuid().ToString('N') + "_" + $batchName)
$stdoutPath = "$batchPath.stdout.txt"
$stderrPath = "$batchPath.stderr.txt"

try {
    [IO.File]::WriteAllBytes($batchPath, [Convert]::FromBase64String('{{base64}}'))

    $process = Start-Process `
        -FilePath 'cmd.exe' `
        -ArgumentList "/c `"$batchPath`"" `
        -Wait `
        -PassThru `
        -NoNewWindow `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath

    if (Test-Path $stdoutPath) {
        $stdout = Get-Content -Raw -Path $stdoutPath
        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            Write-Output $stdout
        }
    }

    if (Test-Path $stderrPath) {
        $stderr = Get-Content -Raw -Path $stderrPath
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Output "STDERR:"
            Write-Output $stderr
        }
    }

    exit $process.ExitCode
}
finally {
    Remove-Item $batchPath -Force -ErrorAction SilentlyContinue
    Remove-Item $stdoutPath -Force -ErrorAction SilentlyContinue
    Remove-Item $stderrPath -Force -ErrorAction SilentlyContinue
}
""";
    }
}

public class FolderCleanupRequest
{
    public string DeviceId { get; set; } = "";
    public string Path { get; set; } = "";
}

public class DeviceActionRequest
{
    public string DeviceId { get; set; } = "";
}

public class BatchTemplateRunRequest
{
    public string Template { get; set; } = "";
    public List<string>? DeviceIds { get; set; }
    public List<int>? StoreCodes { get; set; }
    public bool OnlineOnly { get; set; } = true;
    public string? OsContains { get; set; }
    public bool SelectAllMatching { get; set; }
}
