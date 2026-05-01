using Orchestra.Shared.Dtos;    // <- BU OLSUN

namespace Orchestra.Agent.Services;

public interface ICommandExecutor
{
    Task<CommandResultDto> ExecuteAsync(CommandDto command, CancellationToken token);
}
