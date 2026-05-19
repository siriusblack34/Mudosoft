using Orchestra.Shared.Dtos;    // <- BU OLSUN

namespace Orchestra.Agent.Services;

public interface ICommandExecutor
{
    Task<CommandResultDto> ExecuteAsync(CommandDto command, CancellationToken token);

    // Agent kendi check etti, yeni surum var, kendiliginden update tetikle.
    // Backend'in "trigger" command'ini beklemeden calisir; ayni ExecuteAgentUpdate akisini kullanir.
    void TriggerSelfUpdate(string backendUrl);
}
