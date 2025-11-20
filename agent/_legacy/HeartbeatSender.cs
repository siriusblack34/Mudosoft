using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Options;
using Mudosoft.Shared.Dtos;

namespace Mudosoft.Agent.Services;

public sealed class HeartbeatSender : IHeartbeatSender
{
    private readonly ILogger<HeartbeatSender> _logger;
    private readonly IHttpClientFactory _clientFactory;
    private readonly ISystemInfoCollector _collector;
    private readonly AgentOptions _options;

    public HeartbeatSender(
        ILogger<HeartbeatSender> logger,
        IHttpClientFactory clientFactory,
        ISystemInfoCollector collector,
        IOptions<AgentOptions> options)
    {
        _logger = logger;
        _clientFactory = clientFactory;
        _collector = collector;
        _options = options.Value;
    }

    public async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var dto = await _collector.CollectAsync(cancellationToken);
        var client = _clientFactory.CreateClient("BackendClient");

        _logger.LogDebug("Sending heartbeat for {DeviceId}", dto.DeviceId);

        using var response = await client.PostAsJsonAsync("/api/agent/heartbeat", dto, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Heartbeat failed: {StatusCode}", response.StatusCode);
        }
    }
}
