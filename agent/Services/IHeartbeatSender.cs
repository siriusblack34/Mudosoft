// agent/Services/IHeartbeatSender.cs
namespace Orchestra.Agent.Services;

public interface IHeartbeatSender
{
    Task SendHeartbeatAsync(CancellationToken token);
}
