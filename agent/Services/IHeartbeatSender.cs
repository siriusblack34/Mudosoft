// agent/Services/IHeartbeatSender.cs
namespace Mudosoft.Agent.Services;

public interface IHeartbeatSender
{
    Task SendHeartbeatAsync(CancellationToken token);
}
