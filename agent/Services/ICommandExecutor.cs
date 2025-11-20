using Mudosoft.Shared.Dtos;    // <- BU OLSUN

namespace Mudosoft.Agent.Services;

public interface ICommandExecutor
{
    Task<CommandResultDto> ExecuteAsync(CommandDto command, CancellationToken token);
}
