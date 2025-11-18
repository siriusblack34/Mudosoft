namespace Mudosoft.Agent.Services;

public interface IHeartbeatSender
{
    Task SendHeartbeatAsync(CancellationToken cancellationToken);
}
