using Mudosoft.Shared.Dtos;

namespace Mudosoft.Agent.Services;

public interface ISystemInfoCollector
{
    Task<DeviceHeartbeatDto> CollectAsync(CancellationToken cancellationToken);
}
