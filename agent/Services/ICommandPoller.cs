namespace Orchestra.Agent.Services;

public interface ICommandPoller
{
    Task PollAndExecuteAsync(CancellationToken token);
}
