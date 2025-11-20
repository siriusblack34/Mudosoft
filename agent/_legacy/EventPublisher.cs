using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using IHttpClientFactory = System.Net.Http.IHttpClientFactory;

namespace Mudosoft.Agent.Services;

public sealed class EventPublisher : IEventPublisher
{
    private readonly ILogger<EventPublisher> _logger;
    private readonly IHttpClientFactory _clientFactory;

    public EventPublisher(ILogger<EventPublisher> logger, IHttpClientFactory clientFactory)
    {
        _logger = logger;
        _clientFactory = clientFactory;
    }

    public async Task PublishAsync(DeviceEventDto @event, CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateClient("BackendClient");
        var response = await client.PostAsJsonAsync("/api/agent/events", @event, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to publish event {Type}: {Status}", @event.EventType, response.StatusCode);
        }
    }
}
