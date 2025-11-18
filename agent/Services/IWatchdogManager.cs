namespace Mudosoft.Agent.Services;

public interface IWatchdogManager
{
    void Start(CancellationToken cancellationToken);
}
