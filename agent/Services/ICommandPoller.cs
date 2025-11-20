namespace Mudosoft.Agent.Services;

public interface ICommandPoller
{
    Task PollAndExecuteAsync(CancellationToken token);
}
