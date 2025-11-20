using Mudosoft.Shared.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Models;

namespace Mudosoft.Agent.Services;

public sealed class CommandExecutor : ICommandExecutor
{
    private readonly AgentConfig _config;
    private readonly ILogger<CommandExecutor> _logger;

    public CommandExecutor(IOptions<AgentConfig> cfg, ILogger<CommandExecutor> logger)
    {
        _config = cfg.Value;
        _logger = logger;
    }

    public Task<CommandResultDto> ExecuteAsync(CommandDto cmd, CancellationToken token)
    {
        // 🔥 CommandId artık YOK → Id kullanıyoruz
        _logger.LogWarning("⚙️ Executing CMD {CommandId} → {Type}", cmd.Id, cmd.Type);

        // Şimdilik dummy işlem
        return Task.FromResult(new CommandResultDto
        {
            CommandId = cmd.Id,                 // 🔥 Guid Id
            DeviceId = _config.DeviceId,
            Success = true,
            Output = $"Command '{cmd.Type}' executed (dummy)"
        });
    }
}
