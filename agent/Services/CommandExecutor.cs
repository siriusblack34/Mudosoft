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

                case CommandType.FolderCleanup:
                    _logger.LogInformation("FolderCleanup komutu: {Path}", command.Payload);
                    result = ExecuteFolderCleanup(command.Payload ?? "", result);
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
    /// Writes script to temp file to avoid all quoting/escaping issues.
    /// </summary>
    private CommandResultDto ExecuteShellScript(CommandDto command, CommandResultDto result, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(command.Payload))
        {
            result.Output = "Payload boş olduğu için betik çalıştırılamadı.";
            return result;
        }

        string? tempFile = null;
        try
        {
            // Write payload to temp file to avoid all quoting issues
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                tempFile = Path.Combine(Path.GetTempPath(), $"mudosoft_script_{Guid.NewGuid():N}.ps1");
                File.WriteAllText(tempFile, command.Payload);
            }
            else
            {
                tempFile = Path.Combine(Path.GetTempPath(), $"mudosoft_script_{Guid.NewGuid():N}.sh");
                File.WriteAllText(tempFile, command.Payload);
            }

            var (shell, args) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ("powershell.exe", $"-ExecutionPolicy Bypass -File \"{tempFile}\"")
                : ("/bin/bash", tempFile);

            _logger.LogInformation("Running script: {Shell} {Args}", shell, args);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            // Read output with timeout to prevent hanging
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            
            process.WaitForExit(300000); // 5 minute timeout

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
        finally
        {
            // Cleanup temp file
            if (tempFile != null && File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
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

    /// <summary>
    /// Cleans folder contents (deletes all files and subfolders, keeps the folder itself)
    /// </summary>
    private CommandResultDto ExecuteFolderCleanup(string path, CommandResultDto result)
    {
        try
        {
            // %USERTEMP% özel placeholder - aktif kullanıcının Temp klasörü
            if (path.Contains("%USERTEMP%", StringComparison.OrdinalIgnoreCase))
            {
                var userTempPath = GetLoggedInUserTempPath();
                if (!string.IsNullOrEmpty(userTempPath))
                {
                    path = path.Replace("%USERTEMP%", userTempPath, StringComparison.OrdinalIgnoreCase);
                }
            }
            
            // %TEMP%, %USERPROFILE% gibi değişkenleri genişlet
            path = Environment.ExpandEnvironmentVariables(path);
            _logger.LogInformation("FolderCleanup expanded path: {Path}", path);

            if (!Directory.Exists(path))
            {
                result.Output = $"Directory not found: {path}";
                return result;
            }

            int deletedFiles = 0;
            int deletedFolders = 0;
            long freedBytes = 0;
            var errors = new List<string>();

            // Delete all files
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    freedBytes += fileInfo.Length;
                    File.Delete(file);
                    deletedFiles++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }

            // Delete all subdirectories
            foreach (var dir in Directory.GetDirectories(path))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    deletedFolders++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(dir)}/: {ex.Message}");
                }
            }

            var freedMB = Math.Round(freedBytes / 1024.0 / 1024.0, 2);
            result.Output = System.Text.Json.JsonSerializer.Serialize(new
            {
                path,
                deletedFiles,
                deletedFolders,
                freedMB,
                errors = errors.Take(10).ToList(), // Max 10 errors
                success = errors.Count == 0
            });
            result.Success = errors.Count == 0;
        }
        catch (UnauthorizedAccessException)
        {
            result.Output = $"Access denied: {path}";
        }
        catch (Exception ex)
        {
            result.Output = $"Error cleaning directory: {ex.Message}";
            _logger.LogError(ex, "FolderCleanup error for path: {Path}", path);
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
                _logger.LogError("Update failed: Invalid payload - {Payload}", payload);
                return result;
            }

            var backendUrl = updateInfo.BackendUrl.TrimEnd('/');
            var downloadUrl = $"{backendUrl}/api/updates/download";
            
            // Get current agent directory
            var currentDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var tempDir = Path.Combine(Path.GetTempPath(), "MudoSoftUpdate");
            var zipPath = Path.Combine(tempDir, "agent_update.zip");
            var extractDir = Path.Combine(tempDir, "extracted");

            _logger.LogInformation("=== AGENT UPDATE STARTED ===");
            _logger.LogInformation("Backend URL: {Url}", backendUrl);
            _logger.LogInformation("Current Dir: {Dir}", currentDir);
            _logger.LogInformation("Temp Dir: {Dir}", tempDir);

            // Clean temp directory
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not clean temp dir: {Error}", ex.Message);
            }
            
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(extractDir);

            _logger.LogInformation("Downloading update from {Url}", downloadUrl);

            // Download the update
            long downloadedBytes = 0;
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromMinutes(5);
                var response = httpClient.GetAsync(downloadUrl).GetAwaiter().GetResult();
                
                if (!response.IsSuccessStatusCode)
                {
                    result.Output = $"Download failed: {response.StatusCode}";
                    _logger.LogError("Download failed with status: {Status}", response.StatusCode);
                    return result;
                }

                using (var fs = new FileStream(zipPath, FileMode.Create))
                {
                    response.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                    downloadedBytes = fs.Length;
                }
            }

            _logger.LogInformation("Download complete: {Bytes} bytes", downloadedBytes);

            // Extract zip
            _logger.LogInformation("Extracting to {Path}", extractDir);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, true);
            
            var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            _logger.LogInformation("Extracted {Count} files", extractedFiles.Length);

            // Create a batch file for the update - simpler and more reliable
            var serviceName = "MudosoftAgentService";
            var batchPath = Path.Combine(tempDir, "updater.cmd");
            var logPath = Path.Combine(tempDir, "update.log");
            
            // Escape backslashes for batch file
            var batchContent = $@"@echo off
echo [%date% %time%] Update started > ""{logPath}""

REM Wait for agent to fully process this command
timeout /t 3 /nobreak > nul

REM Stop Tray
echo [%date% %time%] Stopping Tray... >> ""{logPath}""
taskkill /F /IM MudoSoft.Tray.exe 2>nul

REM Stop Agent Service
echo [%date% %time%] Stopping service... >> ""{logPath}""
net stop ""{serviceName}"" 2>> ""{logPath}""
net stop ""MudosoftAgent"" 2>> ""{logPath}""
net stop ""MudoSoft.Agent"" 2>> ""{logPath}""
timeout /t 3 /nobreak > nul

REM Force kill if services didn't stop it (prevents xcopy Access Denied)
echo [%date% %time%] Force killing agent... >> ""{logPath}""
taskkill /F /IM MudoSoft.Agent.exe 2>> ""{logPath}""
timeout /t 2 /nobreak > nul

REM Remove appsettings from extracted to preserve machine-specific config
if exist ""{extractDir}\appsettings.json"" del /F /Q ""{extractDir}\appsettings.json""
if exist ""{extractDir}\appsettings.Development.json"" del /F /Q ""{extractDir}\appsettings.Development.json""

REM Copy files
echo [%date% %time%] Copying files... >> ""{logPath}""
xcopy /E /Y /Q ""{extractDir}\*"" ""{currentDir}\"" >> ""{logPath}"" 2>&1

REM Start Agent Service
echo [%date% %time%] Starting service... >> ""{logPath}""
net start ""{serviceName}"" 2>> ""{logPath}""
net start ""MudosoftAgent"" 2>> ""{logPath}""
net start ""MudoSoft.Agent"" 2>> ""{logPath}""

echo [%date% %time%] Update complete! >> ""{logPath}""
";
            File.WriteAllText(batchPath, batchContent);
            _logger.LogInformation("Batch file created at: {Path}", batchPath);

            // Start the batch file DETACHED from service Job Object
            // On W11, services kill all child processes when stopped.
            // CREATE_BREAKAWAY_FROM_JOB (0x01000000) breaks out of the Job Object.
            _logger.LogInformation("Starting updater with CREATE_BREAKAWAY_FROM_JOB...");
            var pid = StartDetachedProcess("cmd.exe", $"/C \"{batchPath}\"");
            _logger.LogInformation("Update process started with PID: {PID}", pid);

            result.Success = true;
            result.Output = $"Update initiated. Downloaded {downloadedBytes / 1024}KB. Agent will restart in ~10 seconds.";
            _logger.LogInformation("=== AGENT UPDATE COMMAND COMPLETED ===");
        }
        catch (Exception ex)
        {
            result.Output = $"Update failed: {ex.Message}";
            _logger.LogError(ex, "Agent update error: {Message}", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Starts a process detached from the service Job Object using CREATE_BREAKAWAY_FROM_JOB.
    /// This ensures the child process survives when the service is stopped (critical for W11).
    /// </summary>
    private int StartDetachedProcess(string fileName, string arguments)
    {
        const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
        const uint CREATE_NO_WINDOW = 0x08000000;

        var si = new NativeMethods.STARTUPINFO();
        si.cb = System.Runtime.InteropServices.Marshal.SizeOf(si);

        var pi = new NativeMethods.PROCESS_INFORMATION();

        var commandLine = $"{fileName} {arguments}";

        bool success = NativeMethods.CreateProcess(
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            CREATE_BREAKAWAY_FROM_JOB | CREATE_NO_WINDOW,
            IntPtr.Zero,
            null,
            ref si,
            out pi
        );

        if (!success)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            _logger.LogWarning("CREATE_BREAKAWAY_FROM_JOB failed (error {Error}), falling back to normal process start", error);

            // Fallback to normal process start (works on W7 and when Job Object allows it)
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            return proc?.Id ?? 0;
        }

        var pid = pi.dwProcessId;
        // Close handles
        NativeMethods.CloseHandle(pi.hProcess);
        NativeMethods.CloseHandle(pi.hThread);
        return pid;
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX, dwY, dwXSize, dwYSize;
            public int dwXCountChars, dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput, hStdOutput, hStdError;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern bool CreateProcess(
            string? lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation
        );

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
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

    /// <summary>
    /// Aktif kullanıcının Temp klasör yolunu döndürür (SYSTEM hesabıyla çalışırken)
    /// explorer.exe process'inin sahibinden kullanıcı adını alır
    /// </summary>
    private string? GetLoggedInUserTempPath()
    {
        try
        {
            // explorer.exe process'inin sahibini bul - bu aktif kullanıcıdır
            var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT * FROM Win32_Process WHERE Name = 'explorer.exe'");
            
            string? userName = null;
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                // GetOwner metodunu çağır
                var outParams = obj.InvokeMethod("GetOwner", null, null);
                if (outParams != null)
                {
                    userName = outParams["User"]?.ToString();
                    if (!string.IsNullOrEmpty(userName))
                    {
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(userName))
            {
                _logger.LogWarning("No logged-in user found via explorer.exe");
                return null;
            }

            // Kullanıcının Temp klasörünü oluştur
            var userTempPath = $@"C:\Users\{userName}\AppData\Local\Temp";
            _logger.LogInformation("Resolved user temp path for {User}: {Path}", userName, userTempPath);
            
            return userTempPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get logged-in user temp path");
            return null;
        }
    }
}