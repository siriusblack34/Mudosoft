namespace Orchestra.Agent.Services;

public interface IWatchdogManager
{
    void Start(CancellationToken token);
}
