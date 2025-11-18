using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Options;
using Mudosoft.Shared.Dtos;

namespace Mudosoft.Agent.Services;

public sealed class CommandPoller : ICommandPoller
{
    private readonly ILogger<CommandPoller> _logger;
    private readonly IHttpClientFactory _clientFactory;
    private readonly ICommandExecutor _executor;
    private readonly AgentOptions _options;

    public CommandPoller(
        ILogger<CommandPoller> logger,
        IHttpClientFactory clientFactory,
        ICommandExecutor executor,
        IOptions<AgentOptions> options)
    {
        _logger = logger;
        _clientFactory = clientFactory;
        _executor = executor;
        _options = options.Value;
    }

    public async Task PollAndExecuteAsync(CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateClient("BackendClient");

        var response = await client.GetAsync($"/api/agent/commands?deviceId={_options.DeviceId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("No commands or error while polling: {Status}", response.StatusCode);
            return;
        }

        var commands = await response.Content.ReadFromJsonAsync<List<CommandDto>>(cancellationToken: cancellationToken)
                       ?? new List<CommandDto>();

        foreach (var command in commands)
        {
            _logger.LogInformation("Executing command {CommandId} of type {Type}", command.Id, command.Type);
            var result = await _executor.ExecuteAsync(command, cancellationToken);

            // Sonucu backend’e gönder
            await client.PostAsJsonAsync("/api/agent/command-result", result, cancellationToken);
        }
    }
}
