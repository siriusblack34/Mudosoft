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
                    result = ExecuteShellScript(command, result, token); // token eklendi
                    break;

                case CommandType.Shutdown:
                    _logger.LogInformation("Shutdown komutu alındı. Simülasyon yapılıyor...");
                    result.Success = true;
                    result.Output = "Shutdown komutu başarıyla çalıştırıldı (Simülasyon).";
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
}