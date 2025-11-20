using Mudosoft.Agent.Models;
using Mudosoft.Shared.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Net.Http.Json;

namespace Mudosoft.Agent.Services;

public sealed class CommandPoller : ICommandPoller
{
    private readonly HttpClient _http;
    private readonly AgentConfig _config;
    private readonly ILogger<CommandPoller> _logger;
    private readonly ICommandExecutor _executor;

    public CommandPoller(
        IHttpClientFactory httpFactory,
        IOptions<AgentConfig> options,
        ICommandExecutor executor,
        ILogger<CommandPoller> logger)
    {
        _http = httpFactory.CreateClient();
        _config = options.Value;
        _executor = executor;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_config.BackendUrl))
            _http.BaseAddress = new Uri(_config.BackendUrl);
    }

    public async Task PollAndExecuteAsync(CancellationToken token)
    {
        try
        {
            var url = $"api/agent/commands?deviceId={_config.DeviceId}";
            var commands = await _http.GetFromJsonAsync<List<CommandDto>>(url, token);

            if (commands is null || commands.Count == 0)
                return;

            foreach (var cmd in commands)
            {
                // 🔥 CommandId artık yok → Id
                _logger.LogInformation("➡️ Received command {CommandId} → {Type}", cmd.Id, cmd.Type);

                var result = await _executor.ExecuteAsync(cmd, token);

                await _http.PostAsJsonAsync("api/agent/command-result", result, token);

                _logger.LogInformation("⬅️ Result sent for {CommandId}", cmd.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command polling failed");
        }
    }
}
