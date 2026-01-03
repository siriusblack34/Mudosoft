using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Mudosoft.Agent.Interfaces;
using Mudosoft.Shared.Dtos;
using Mudosoft.Shared.Enums;
using System.Runtime.InteropServices; 

namespace Mudosoft.Agent.Services;

public sealed class CommandExecutor : ICommandExecutor
{
    private readonly ILogger<CommandExecutor> _logger;

    public CommandExecutor(ILogger<CommandExecutor> logger)
    {
        _logger = logger;
    }

    // HATA ÇÖZÜMÜ: CancellationToken token parametresi eklendi
   public Task<CommandResultDto> ExecuteAsync(CommandDto command, CancellationToken token)
    {
        var result = new CommandResultDto
        {
            CommandId = command.Id,
            DeviceId = command.DeviceId,
            Success = false,
            Output = "",
            CommandType = command.Type // <-- Artık CommandResultDto'da CommandType var
        };

        try
        {
            switch (command.Type)
            {
                case CommandType.Reboot:
                    _logger.LogInformation("Reboot komutu alındı. Simülasyon yapılıyor...");
                    result.Success = true;
                    result.Output = "Reboot komutu başarıyla çalıştırıldı (Simülasyon).";
                    break;

                case CommandType.ExecuteScript:
                    _logger.LogInformation("ExecuteScript komutu alındı.");
                    result = ExecuteShellScript(command, result, token);
                    break;

                case CommandType.Shutdown:
                    _logger.LogInformation("Shutdown komutu alındı. Simülasyon yapılıyor...");
                    result.Success = true;
                    result.Output = "Shutdown komutu başarıyla çalıştırıldı (Simülasyon).";
                    break;

                // FILE OPERATIONS
                case CommandType.FileList:
                    _logger.LogInformation("FileList komutu: {Path}", command.Payload);
                    result = ExecuteFileList(command.Payload ?? "C:\\", result);
                    break;

                case CommandType.FileRead:
                    _logger.LogInformation("FileRead komutu: {Path}", command.Payload);
                    result = ExecuteFileRead(command.Payload ?? "", result);
                    break;

                case CommandType.FileWrite:
                    _logger.LogInformation("FileWrite komutu: {Path}", command.Payload);
                    result = ExecuteFileWrite(command.Payload ?? "", result);
                    break;

                case CommandType.FileDelete:
                    _logger.LogInformation("FileDelete komutu: {Path}", command.Payload);
                    result = ExecuteFileDelete(command.Payload ?? "", result);
                    break;

                case CommandType.FolderCreate:
                    _logger.LogInformation("FolderCreate komutu: {Path}", command.Payload);
                    result = ExecuteFolderCreate(command.Payload ?? "", result);
                    break;

                // AGENT MANAGEMENT
                case CommandType.UpdateAgent:
                    _logger.LogInformation("UpdateAgent komutu alındı: {Payload}", command.Payload);
                    result = ExecuteAgentUpdate(command.Payload ?? "", result);
                    break;

                default:
                    result.Output = $"Bilinmeyen komut tipi: {command.Type}";
                    _logger.LogWarning(result.Output);
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Output = $"Komut yürütülürken hata oluştu: {ex.Message}";
            _logger.LogError(ex, "Komut yürütme hatası: {CommandId}", command.Id);
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Betiği OS'ye özgü kabukta (Shell) çalıştırır ve çıktıyı yakalar.
    /// </summary>
    private CommandResultDto ExecuteShellScript(CommandDto command, CommandResultDto result, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(command.Payload))
        {
            result.Output = "Payload boş olduğu için betik çalıştırılamadı.";
            return result;
        }

        // Platform tespiti ve ilgili kabuk (shell) seçimi
        var (shell, argsPrefix) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("powershell.exe", "-Command") // Windows
            : ("/bin/bash", "-c");          // Linux/macOS

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = $"{argsPrefix} \"{command.Payload}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            // Çıktıyı oku
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            
            // token'ı kontrol et ve limitli bekleme yap
            process.WaitForExit(30000); 

            result.Output = (string.IsNullOrWhiteSpace(output) ? "" : output) +
                            (string.IsNullOrWhiteSpace(error) ? "" : $"Hata: {error}");
            
            result.Success = process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            result.Output = $"Betik çalıştırma sürecinde kritik hata: {ex.Message}";
            _logger.LogError(ex, "Betik çalıştırma başarısız.");
            result.Success = false;
        }

        return result;
    }

    #region File Operations

    /// <summary>
    /// Lists files and folders in the specified path
    /// </summary>
    private CommandResultDto ExecuteFileList(string path, CommandResultDto result)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                result.Output = $"Directory not found: {path}";
                return result;
            }

            var items = new List<object>();

            // Get directories
            foreach (var dir in Directory.GetDirectories(path))
            {
                var info = new DirectoryInfo(dir);
                items.Add(new
                {
                    name = info.Name,
                    fullPath = info.FullName,
                    isDirectory = true,
                    sizeBytes = 0L,
                    lastModified = info.LastWriteTimeUtc.ToString("o")
                });
            }

            // Get files
            foreach (var file in Directory.GetFiles(path))
            {
                var info = new FileInfo(file);
                items.Add(new
                {
                    name = info.Name,
                    fullPath = info.FullName,
                    isDirectory = false,
                    sizeBytes = info.Length,
                    lastModified = info.LastWriteTimeUtc.ToString("o")
                });
            }

            result.Output = System.Text.Json.JsonSerializer.Serialize(items);
            result.Success = true;
        }
        catch (UnauthorizedAccessException)
        {
            result.Output = $"Access denied: {path}";
        }
        catch (Exception ex)
        {
            result.Output = $"Error listing directory: {ex.Message}";
            _logger.LogError(ex, "FileList error for path: {Path}", path);
        }

        return result;
    }

    /// <summary>
    /// Reads file content and returns as Base64
    /// </summary>
    private CommandResultDto ExecuteFileRead(string path, CommandResultDto result)
    {
        try
        {
            if (!File.Exists(path))
            {
                result.Output = $"File not found: {path}";
                return result;
            }

            var bytes = File.ReadAllBytes(path);
            result.Output = Convert.ToBase64String(bytes);
            result.Success = true;
        }
        catch (UnauthorizedAccessException)
        {
            result.Output = $"Access denied: {path}";
        }
        catch (Exception ex)
        {
            result.Output = $"Error reading file: {ex.Message}";
            _logger.LogError(ex, "FileRead error for path: {Path}", path);
        }

        return result;
    }

    /// <summary>
    /// Writes file content (expects JSON: {path: string, content: base64string})
    /// </summary>
    private CommandResultDto ExecuteFileWrite(string payload, CommandResultDto result)
    {
        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<FileWritePayload>(payload);
            if (data == null || string.IsNullOrEmpty(data.Path))
            {
                result.Output = "Invalid payload: path required";
                return result;
            }

            var bytes = Convert.FromBase64String(data.Content ?? "");
            File.WriteAllBytes(data.Path, bytes);
            result.Output = $"File written: {data.Path}";
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Output = $"Error writing file: {ex.Message}";
            _logger.LogError(ex, "FileWrite error");
        }

        return result;
    }

    /// <summary>
    /// Deletes file or folder
    /// </summary>
    private CommandResultDto ExecuteFileDelete(string path, CommandResultDto result)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                result.Output = $"File deleted: {path}";
                result.Success = true;
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                result.Output = $"Directory deleted: {path}";
                result.Success = true;
            }
            else
            {
                result.Output = $"Path not found: {path}";
            }
        }
        catch (UnauthorizedAccessException)
        {
            result.Output = $"Access denied: {path}";
        }
        catch (Exception ex)
        {
            result.Output = $"Error deleting: {ex.Message}";
            _logger.LogError(ex, "FileDelete error for path: {Path}", path);
        }

        return result;
    }

    /// <summary>
    /// Creates a new folder
    /// </summary>
    private CommandResultDto ExecuteFolderCreate(string path, CommandResultDto result)
    {
        try
        {
            if (Directory.Exists(path))
            {
                result.Output = $"Directory already exists: {path}";
                return result;
            }

            Directory.CreateDirectory(path);
            result.Output = $"Directory created: {path}";
            result.Success = true;
        }
        catch (UnauthorizedAccessException)
        {
            result.Output = $"Access denied: {path}";
        }
        catch (Exception ex)
        {
            result.Output = $"Error creating directory: {ex.Message}";
            _logger.LogError(ex, "FolderCreate error for path: {Path}", path);
        }

        return result;
    }

    #endregion

    #region Agent Update

    /// <summary>
    /// Downloads and applies agent update
    /// Payload: JSON with { backendUrl: string, version: string }
    /// </summary>
    private CommandResultDto ExecuteAgentUpdate(string payload, CommandResultDto result)
    {
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var updateInfo = System.Text.Json.JsonSerializer.Deserialize<UpdatePayload>(payload, options);
            if (updateInfo == null || string.IsNullOrEmpty(updateInfo.BackendUrl))
            {
                result.Output = $"Invalid payload: backendUrl required. Payload was: {payload}";
                return result;
            }

            var backendUrl = updateInfo.BackendUrl.TrimEnd('/');
            var downloadUrl = $"{backendUrl}/api/updates/download";
            
            // Get current agent directory
            var currentDir = AppContext.BaseDirectory;
            var tempDir = Path.Combine(Path.GetTempPath(), "MudoSoftUpdate");
            var zipPath = Path.Combine(tempDir, "agent_update.zip");
            var extractDir = Path.Combine(tempDir, "extracted");

            // Clean temp directory
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(extractDir);

            _logger.LogInformation("Downloading update from {Url}", downloadUrl);

            // Download the update
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromMinutes(5);
                var response = httpClient.GetAsync(downloadUrl).GetAwaiter().GetResult();
                
                if (!response.IsSuccessStatusCode)
                {
                    result.Output = $"Download failed: {response.StatusCode}";
                    return result;
                }

                using (var fs = new FileStream(zipPath, FileMode.Create))
                {
                    response.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                }
            }

            _logger.LogInformation("Download complete, extracting to {Path}", extractDir);

            // Extract zip
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, true);

            // Use PowerShell scheduled task approach for Session 0 compatibility
            var serviceName = "MudosoftAgentService";
            var taskName = "MudoSoftAgentUpdater";
            
            // PowerShell script that will be run by the scheduled task
            var psScriptPath = Path.Combine(tempDir, "updater.ps1");
            var psContent = $@"
# MudoSoft Agent Updater Script
Start-Sleep -Seconds 3

# 1. Stop Tray first (taskkill works across all user sessions)
Write-Host 'Stopping Tray...'
taskkill /F /IM MudoSoft.Tray.exe 2>$null
Start-Sleep -Seconds 2

# 2. Stop Agent Service
Write-Host 'Stopping Agent service...'
Stop-Service -Name '{serviceName}' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# 3. Copy all files (Agent + Tray)
Write-Host 'Copying update files...'
Copy-Item -Path '{extractDir}\*' -Destination '{currentDir}' -Force -Recurse

# 4. Restart Agent Service
Write-Host 'Starting Agent service...'
Start-Service -Name '{serviceName}'

# 5. Restart Tray and RDHelper for logged-in users
Write-Host 'Starting Tray and RDHelper for users...'
$trayPath = Join-Path '{currentDir}' 'MudoSoft.Tray.exe'
$helperPath = Join-Path '{currentDir}' 'MudoSoft.RDHelper.exe'

# Simple start - Registry will auto-start on next login if this fails
if (Test-Path $trayPath) {{
    Start-Process -FilePath $trayPath -WindowStyle Hidden -ErrorAction SilentlyContinue
}}
if (Test-Path $helperPath) {{
    Start-Process -FilePath $helperPath -WindowStyle Hidden -ErrorAction SilentlyContinue
}}

# 6. Cleanup
Unregister-ScheduledTask -TaskName '{taskName}' -Confirm:$false -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3
Remove-Item -Path '{tempDir}' -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Update complete!'
";
            File.WriteAllText(psScriptPath, psContent);

            _logger.LogInformation("Creating scheduled task for update");

            // Create a one-time scheduled task to run immediately as SYSTEM
            var createTaskCmd = $@"schtasks /Create /TN ""{taskName}"" /TR ""powershell.exe -ExecutionPolicy Bypass -File '{psScriptPath}'"" /SC ONCE /ST 00:00 /RU SYSTEM /F";
            var runTaskCmd = $@"schtasks /Run /TN ""{taskName}""";

            // Create the task
            var createPsi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C {createTaskCmd}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            var createProcess = Process.Start(createPsi);
            createProcess?.WaitForExit(5000);
            
            _logger.LogInformation("Scheduled task created, running it now");

            // Run the task
            var runPsi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C {runTaskCmd}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            var runProcess = Process.Start(runPsi);
            runProcess?.WaitForExit(5000);

            result.Success = true;
            result.Output = $"Update initiated via scheduled task. Agent will restart shortly.";
        }
        catch (Exception ex)
        {
            result.Output = $"Update failed: {ex.Message}";
            _logger.LogError(ex, "Agent update error");
        }

        return result;
    }

    private class UpdatePayload
    {
        public string? BackendUrl { get; set; }
        public string? Version { get; set; }
    }

    #endregion

    /// <summary>
    /// Payload structure for FileWrite command
    /// </summary>
    private class FileWritePayload
    {
        public string? Path { get; set; }
        public string? Content { get; set; }
    }
}