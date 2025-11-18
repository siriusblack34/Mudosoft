using Mudosoft.Shared.Dtos;

namespace Mudosoft.Agent.Services;

public interface ICommandExecutor
{
    Task<CommandResultDto> ExecuteAsync(CommandDto command, CancellationToken cancellationToken);
}
