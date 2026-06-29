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

            // UNC erisim gerektiren bat'lara net use ile credential enjekte et
            var injected = InjectNetUseCredentials(batContent);
            var injectedBytes = Encoding.Default.GetBytes(injected);

            if (target.Mode == BatchTargetMode.Agent && !string.IsNullOrWhiteSpace(target.DeviceId))
            {
                await RunAgentAsync(target, injected);
            }
            else if (!string.IsNullOrWhiteSpace(target.IpAddress))
            {
                await RunAgentlessAsync(target, injectedBytes, exec.FileName);
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

    private static string InjectNetUseCredentials(string batContent)
    {
        var uncShares = System.Text.RegularExpressions.Regex
            .Matches(batContent, @"\\\\([^\\\/\s""]+)\\([^\\\/\s""]+)")
            .Select(m => $@"\\{m.Groups[1].Value}\{m.Groups[2].Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (uncShares.Count == 0) return batContent;

        var user = Environment.GetEnvironmentVariable("WMI_USER") ?? @"MUDODMN\mudoadmtd";
        var pass = Environment.GetEnvironmentVariable("WMI_PASSWORD") ?? "";

        var pre  = string.Join("\r\n", uncShares.Select(s => $"net use {s} /user:{user} {pass} /persistent:no 2>nul"));
        var post = string.Join("\r\n", uncShares.Select(s => $"net use {s} /delete /y >nul 2>nul"));

        return $"@echo off\r\n{pre}\r\n{batContent.TrimStart()}\r\n{post}\r\n";
    }

    /// <summary>
    /// Agent'siz yol: \\IP\C$\temp'e bat'i kopyala, WMI Win32_Process Create ile cmd /c calistir, log dosyasini oku.
    /// Backend host'unun WMI/SMB credentials'ini kullanir (RemoteInstallController desenidir).
    /// </summary>
    private async Task RunAgentlessAsync(BatchTargetResult target, byte[] batBytes, string originalFileName)
    {
        var ip = target.IpAddress!;
        var safeFile = "orchestra_" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".bat";
        var wrapFile = "wrap_" + safeFile;
        var logFile  = safeFile + ".log";
        var doneFile = safeFile + ".done";
        var remoteTemp = $@"\\{ip}\C$\temp";

        try
        {
            // 1. Remote C:\temp olustur (yoksa) ve bat'i kopyala
            Directory.CreateDirectory(remoteTemp);
            await File.WriteAllBytesAsync(Path.Combine(remoteTemp, safeFile), batBytes);

            // 2. Wrapper bat yaz (credential enjeksiyonu zaten batBytes icinde)
            var wrapContent =
                $"@echo off\r\n" +
                $"call \"C:\\temp\\{safeFile}\" > \"C:\\temp\\{logFile}\" 2>&1\r\n" +
                $"echo %ERRORLEVEL% > \"C:\\temp\\{doneFile}\"\r\n";
            await File.WriteAllTextAsync(Path.Combine(remoteTemp, wrapFile), wrapContent, System.Text.Encoding.ASCII);

            // 3. WMI ile cmd.exe uzaktan calistir (DCOM/RPC — WSMan gerekmez)
            uint wmiReturn = await Task.Run(() =>
            {
                var scope = new System.Management.ManagementScope($@"\\{ip}\root\cimv2");
                scope.Connect();
                using var cls = new System.Management.ManagementClass(scope, new System.Management.ManagementPath("Win32_Process"), null);
                using var inP = cls.GetMethodParameters("Create");
                inP["CommandLine"] = $@"cmd.exe /c C:\temp\{wrapFile}";
                using var outP = cls.InvokeMethod("Create", inP, null);
                return (uint)outP["ReturnValue"];
            });

            if (wmiReturn != 0)
            {
                target.Phase = BatchPhase.Error;
                target.Error = $"WMI Win32_Process.Create hatasi (kod: {wmiReturn})";
                return;
            }

            // 4. Done marker'i bekle (max 5dk)
            var donePath = Path.Combine(remoteTemp, doneFile);
            bool found = false;
            for (int i = 0; i < 400; i++)
            {
                await Task.Delay(3000);
                if (File.Exists(donePath)) { found = true; break; }
            }

            if (!found)
            {
                target.Phase = BatchPhase.Error;
                target.Error = "Timeout: bat dosyasi 20 dakika icinde tamamlanmadi";
                return;
            }

            // 5. Exit kodu ve log oku
            int.TryParse((await File.ReadAllTextAsync(donePath)).Trim(), out int exitCode);
            var logPath = Path.Combine(remoteTemp, logFile);
            var output = File.Exists(logPath)
                ? (await File.ReadAllTextAsync(logPath, System.Text.Encoding.Default)).Trim('\r', '\n', ' ')
                : "";

            target.Output = output;
            target.Phase  = exitCode == 0 ? BatchPhase.Done : BatchPhase.Error;
            if (exitCode != 0) target.Error = $"Bat exit code: {exitCode}";
        }
        catch (Exception ex)
        {
            target.Phase = BatchPhase.Error;
            target.Error = ex.Message;
        }
        finally
        {
            foreach (var f in new[] { safeFile, wrapFile, logFile, doneFile })
                try { File.Delete(Path.Combine(remoteTemp, f)); } catch { }
        }
    }
}
