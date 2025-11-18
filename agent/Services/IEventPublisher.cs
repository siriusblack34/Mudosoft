using Mudosoft.Shared.Dtos;

namespace Mudosoft.Agent.Services;

public interface IEventPublisher
{
    Task PublishAsync(DeviceEventDto @event, CancellationToken cancellationToken);
}
