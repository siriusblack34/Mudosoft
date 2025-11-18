using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Mudosoft.Shared.Dtos;
using Mudosoft.Shared.Enums;

namespace Mudosoft.Agent.Services;

public sealed class CommandExecutor : ICommandExecutor
{
    private readonly ILogger<CommandExecutor> _logger;

    public CommandExecutor(ILogger<CommandExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<CommandResultDto> ExecuteAsync(CommandDto command, CancellationToken cancellationToken)
    {
        var result = new CommandResultDto
        {
            CommandId = command.Id,
            DeviceId = command.DeviceId,
            ExecutedAtUtc = DateTime.UtcNow
        };

        try
        {
            switch (command.Type)
            {
                case CommandType.Reboot:
                    // TODO: shutdown /r
                    result.Success = true;
                    result.Output = "Reboot scheduled.";
                    break;

                case CommandType.RestartService:
                    // TODO: ServiceController kullan
                    result.Success = true;
                    result.Output = "Service restart not implemented yet.";
                    break;

                case CommandType.RunPowerShell:
                    result = await RunProcessAsync("powershell.exe", "-NoProfile -Command " + command.ArgumentsJson, command);
                    break;

                case CommandType.RunBatch:
                    result = await RunProcessAsync("cmd.exe", "/C " + command.ArgumentsJson, command);
                    break;

                case CommandType.CopyFile:
                    // TODO: argümanı parse edip File.Copy yap
                    result.Success = true;
                    result.Output = "CopyFile not implemented yet.";
                    break;

                case CommandType.CustomPosMaintenance:
                    // TODO: POS özel scriptler
                    result.Success = true;
                    result.Output = "POS maintenance placeholder.";
                    break;

                default:
                    result.Success = false;
                    result.Error = $"Unknown command type: {command.Type}";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command {CommandId}", command.Id);
            result.Success = false;
            result.Error = ex.ToString();
        }

        return result;
    }

    private async Task<CommandResultDto> RunProcessAsync(string fileName, string arguments, CommandDto command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        string output = await proc.StandardOutput.ReadToEndAsync();
        string error = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return new CommandResultDto
        {
            CommandId = command.Id,
            DeviceId = command.DeviceId,
            ExecutedAtUtc = DateTime.UtcNow,
            Success = proc.ExitCode == 0,
            Output = output,
            Error = string.IsNullOrWhiteSpace(error) ? null : error
        };
    }
}
