using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Shared.Dtos;
using Orchestra.Shared.Enums;

namespace Orchestra.Backend.Services;

public enum BatchTargetMode
{
    Agent,
    Agentless
}

public enum BatchPhase
{
    Pending,
    Running,
    Done,
    Error
}

public class BatchTargetResult
{
    public string Key { get; set; } = "";          // deviceId veya ip
    public BatchTargetMode Mode { get; set; }
    public string? DeviceId { get; set; }
    public string? IpAddress { get; set; }
    public string? Hostname { get; set; }
    public string? StoreCode { get; set; }
    public BatchPhase Phase { get; set; } = BatchPhase.Pending;
    public string Output { get; set; } = "";
    public string? Error { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CommandId { get; set; }            // agent yolunda
}

public class BatchExecution
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<BatchTargetResult> Targets { get; set; } = new();
}

public class BatchTargetRequest
{
    public string? DeviceId { get; set; }
    public string? IpAddress { get; set; }
    public string? StoreCode { get; set; }
    public string? Hostname { get; set; }
}

public class BatchRunRequest
{
    public string FileName { get; set; } = "script.bat";
    public string ContentBase64 { get; set; } = ""; // bat icerigi base64
    public List<BatchTargetRequest> Targets { get; set; } = new();
}

/// <summary>
/// Bat dosyalarini hem agent'li (CommandQueue) hem agent'siz (WMI + admin share) cihazlarda calistirir.
/// In-memory yurutme deposu kullanir; restart'a kadar gecerli.
/// </summary>
public class BatchExecutionService
{
    private readonly ILogger<BatchExecutionService> _logger;
    private readonly CommandQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly ConcurrentDictionary<string, BatchExecution> _executions = new();

    public BatchExecutionService(
        ILogger<BatchExecutionService> logger,
        CommandQueue queue,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _queue = queue;
        _scopeFactory = scopeFactory;
    }

    public async Task<BatchExecution> StartAsync(BatchRunRequest request, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(request.ContentBase64))
            throw new ArgumentException("Bat icerigi bos olamaz");

        if (request.Targets == null || request.Targets.Count == 0)
            throw new ArgumentException("En az bir target gerekli");

        // Decode + temel sanity check
        byte[] batBytes;
        try
        {
            batBytes = Convert.FromBase64String(request.ContentBase64);
        }
        catch
        {
            throw new ArgumentException("ContentBase64 gecerli base64 degil");
        }

        if (batBytes.Length == 0)
            throw new ArgumentException("Bat icerigi bos");

        if (batBytes.Length > 5 * 1024 * 1024)
            throw new ArgumentException("Bat dosyasi 5MB'tan buyuk");

        var execution = new BatchExecution
        {
            FileName = string.IsNullOrWhiteSpace(request.FileName) ? "script.bat" : request.FileName,
            CreatedBy = createdBy
        };

        // Resolve targets — varsa DB'den deviceId/ip dolduralim
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            foreach (var t in request.Targets)
            {
                var resolved = await ResolveTargetAsync(db, t);
                if (resolved != null)
                    execution.Targets.Add(resolved);
            }
        }

        if (execution.Targets.Count == 0)
            throw new ArgumentException("Gecerli target bulunamadi");

        _executions[execution.Id] = execution;

        // Fire-and-forget — her target paralel calissin
        var batContent = Encoding.UTF8.GetString(batBytes);
        // BOM varsa kirp
        if (batContent.Length > 0 && batContent[0] == '﻿')
            batContent = batContent.Substring(1);

        foreach (var target in execution.Targets)
        {
            _ = Task.Run(() => RunTargetAsync(execution, target, batContent, batBytes));
        }

        _logger.LogInformation("Batch execution baslatildi: {Id} TargetCount={Count} CreatedBy={User}",
            execution.Id, execution.Targets.Count, createdBy);

        return execution;
    }

    public BatchExecution? Get(string executionId)
    {
        _executions.TryGetValue(executionId, out var ex);
        return ex;
    }

    public IEnumerable<BatchExecution> List()
    {
        return _executions.Values.OrderByDescending(e => e.CreatedAtUtc);
    }

    private static async Task<BatchTargetResult?> ResolveTargetAsync(OrchestraDbContext db, BatchTargetRequest t)
    {
        // 1) DeviceId ile gelirse: agent yolu (cihazin online olmasi onerilir ama zorunlu degil)
        if (!string.IsNullOrWhiteSpace(t.DeviceId))
        {
            var dev = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == t.DeviceId);
            if (dev != null)
            {
                return new BatchTargetResult
                {
                    Key = dev.Id,
                    Mode = BatchTargetMode.Agent,
                    DeviceId = dev.Id,
                    IpAddress = dev.IpAddress,
                    Hostname = dev.Hostname,
                    StoreCode = dev.StoreCode.ToString()
                };
            }
        }

        // 2) IP ile gelirse: agentless yolu (DB'de varsa metadata zenginlestirelim)
        if (!string.IsNullOrWhiteSpace(t.IpAddress))
        {
            var ip = t.IpAddress.Trim();
            var dev = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.IpAddress == ip);
            return new BatchTargetResult
            {
                Key = ip,
                Mode = BatchTargetMode.Agentless,
                DeviceId = dev?.Id,
                IpAddress = ip,
                Hostname = dev?.Hostname ?? t.Hostname,
                StoreCode = dev?.StoreCode.ToString() ?? t.StoreCode
            };
        }

        return null;
    }

    private async Task RunTargetAsync(BatchExecution exec, BatchTargetResult target, string batContent, byte[] batBytes)
    {
        try
        {
            target.Phase = BatchPhase.Running;
            target.StartedAtUtc = DateTime.UtcNow;

            if (target.Mode == BatchTargetMode.Agent && !string.IsNullOrWhiteSpace(target.DeviceId))
            {
                await RunAgentAsync(target, batContent);
            }
            else if (!string.IsNullOrWhiteSpace(target.IpAddress))
            {
                await RunAgentlessAsync(target, batBytes, exec.FileName);
            }
            else
            {
                target.Phase = BatchPhase.Error;
                target.Error = "Target ne deviceId ne IP icerdi";
            }
        }
        catch (Exception ex)
        {
            target.Phase = BatchPhase.Error;
            target.Error = ex.Message;
            _logger.LogError(ex, "Batch target run hatasi: {Key}", target.Key);
        }
        finally
        {
            target.CompletedAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Agent'li yol: CommandQueue'ya ExecuteBatch koy, CommandResultRecords tablosundan polling ile sonucu al.
    /// </summary>
    private async Task RunAgentAsync(BatchTargetResult target, string batContent)
    {
        var commandId = Guid.NewGuid();
        target.CommandId = commandId;

        _queue.Enqueue(new CommandDto
        {
            Id = commandId,
            DeviceId = target.DeviceId!,
            Type = CommandType.ExecuteBatch,
            Payload = batContent,
            CreatedAtUtc = DateTime.UtcNow
        });

        // Sonucu poll et — max 6 dakika (agent timeout 5dk + transport)
        for (int i = 0; i < 360; i++)
        {
            await Task.Delay(1000);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
            var result = await db.CommandResultRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.CommandId == commandId);

            if (result != null)
            {
                target.Output = result.Output ?? "";
                target.Phase = result.Success ? BatchPhase.Done : BatchPhase.Error;
                if (!result.Success && string.IsNullOrEmpty(target.Error))
                    target.Error = "Bat exit code != 0";
                return;
            }
        }

        target.Phase = BatchPhase.Error;
        target.Error = "Agent zaman asimi (6dk) — sonuc gelmedi";
    }

    /// <summary>
    /// Agent'siz yol: \\IP\C$\temp'e bat'i kopyala, WMI Win32_Process Create ile cmd /c calistir, log dosyasini oku.
    /// Backend host'unun WMI/SMB credentials'ini kullanir (RemoteInstallController desenidir).
    /// </summary>
    private async Task RunAgentlessAsync(BatchTargetResult target, byte[] batBytes, string originalFileName)
    {
        var ip = target.IpAddress!;
        var safeFile = "orchestra_" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".bat";
        var localBatPath = Path.Combine(Path.GetTempPath(), safeFile);
        var psPath = Path.Combine(Path.GetTempPath(), $"orchestra_run_{Guid.NewGuid():N}.ps1");

        try
        {
            // Bat'i wrap et: original'i icerikten yaz, sonunda DONE marker + log redirect
            // Original bat'in cikti'sini yakalamak icin remote tarafta cmd /c ... > log seklinde calistiriyoruz
            await File.WriteAllBytesAsync(localBatPath, batBytes);

            var script = $@"
$ErrorActionPreference = 'Stop'
$ip = '{ip}'
$batSource = '{localBatPath.Replace("'", "''")}'
$remoteFile = '{safeFile}'
$logFile = $remoteFile + '.log'
$doneFile = $remoteFile + '.done'
try {{
    $remoteTempUnc = ""\\$ip\C$\temp""
    if (!(Test-Path $remoteTempUnc)) {{ New-Item -Path $remoteTempUnc -ItemType Directory -Force | Out-Null }}

    # bat'i remote temp'e kopyala
    Copy-Item -Path $batSource -Destination ""$remoteTempUnc\$remoteFile"" -Force

    # Wrapper bat: original'i calistir, ciktiyi log'a yaz, bitince done marker olustur
    $wrapper = ""@echo off`r`ncall ""C:\temp\$remoteFile"" > ""C:\temp\$logFile"" 2>&1`r`necho %ERRORLEVEL% > ""C:\temp\$doneFile""`r`n""
    [System.IO.File]::WriteAllText(""$remoteTempUnc\wrap_$remoteFile"", $wrapper, [System.Text.Encoding]::ASCII)

    # WMI ile uzaktan calistir (schtasks EDR alert tetikliyor)
    $result = Invoke-WmiMethod -ComputerName $ip -Class Win32_Process -Name Create -ArgumentList ""cmd.exe /c C:\temp\wrap_$remoteFile"" 2>&1
    if ($result.ReturnValue -ne 0) {{
        Write-Output ""__ERR__: WMI process create failed (return=$($result.ReturnValue))""
        return
    }}

    # Done marker'i bekle (max ~5dk)
    $found = $false
    for ($i = 0; $i -lt 100; $i++) {{
        Start-Sleep -Seconds 3
        if (Test-Path ""$remoteTempUnc\$doneFile"") {{ $found = $true; break }}
    }}

    if (-not $found) {{
        Write-Output ""__ERR__: Timeout — bat 5dk icinde tamamlanmadi""
        return
    }}

    $exitCode = (Get-Content ""$remoteTempUnc\$doneFile"" -Raw).Trim()
    $logContent = ''
    if (Test-Path ""$remoteTempUnc\$logFile"") {{
        $logContent = Get-Content ""$remoteTempUnc\$logFile"" -Raw
    }}

    Write-Output ""__EXIT__: $exitCode""
    Write-Output ""__OUTPUT_BEGIN__""
    Write-Output $logContent
    Write-Output ""__OUTPUT_END__""

    # Cleanup remote
    Remove-Item ""$remoteTempUnc\$remoteFile"" -Force -ErrorAction SilentlyContinue
    Remove-Item ""$remoteTempUnc\wrap_$remoteFile"" -Force -ErrorAction SilentlyContinue
    Remove-Item ""$remoteTempUnc\$logFile"" -Force -ErrorAction SilentlyContinue
    Remove-Item ""$remoteTempUnc\$doneFile"" -Force -ErrorAction SilentlyContinue
}} catch {{
    Write-Output ""__ERR__: $($_.Exception.Message)""
}}
";
            await File.WriteAllTextAsync(psPath, script);

            var (_, output) = await RunPowerShellFileAsync(psPath);
            ParseAgentlessOutput(target, output);
        }
        finally
        {
            try { if (File.Exists(localBatPath)) File.Delete(localBatPath); } catch { }
            try { if (File.Exists(psPath)) File.Delete(psPath); } catch { }
        }
    }

    private static void ParseAgentlessOutput(BatchTargetResult target, string output)
    {
        var errIdx = output.IndexOf("__ERR__:", StringComparison.Ordinal);
        if (errIdx >= 0)
        {
            target.Phase = BatchPhase.Error;
            target.Error = output.Substring(errIdx + "__ERR__:".Length).Trim();
            return;
        }

        var exitIdx = output.IndexOf("__EXIT__:", StringComparison.Ordinal);
        var beginIdx = output.IndexOf("__OUTPUT_BEGIN__", StringComparison.Ordinal);
        var endIdx = output.IndexOf("__OUTPUT_END__", StringComparison.Ordinal);

        int exitCode = -1;
        if (exitIdx >= 0)
        {
            var endLine = output.IndexOf('\n', exitIdx);
            var rawExit = endLine > 0
                ? output.Substring(exitIdx + "__EXIT__:".Length, endLine - exitIdx - "__EXIT__:".Length).Trim()
                : output.Substring(exitIdx + "__EXIT__:".Length).Trim();
            int.TryParse(rawExit, out exitCode);
        }

        if (beginIdx >= 0 && endIdx > beginIdx)
        {
            var start = beginIdx + "__OUTPUT_BEGIN__".Length;
            target.Output = output.Substring(start, endIdx - start).Trim('\r', '\n', ' ');
        }

        target.Phase = exitCode == 0 ? BatchPhase.Done : BatchPhase.Error;
        if (exitCode != 0)
            target.Error = $"Bat exit code: {exitCode}";
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellFileAsync(string scriptPath)
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

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout + (string.IsNullOrEmpty(stderr) ? "" : "\n" + stderr));
    }
}
