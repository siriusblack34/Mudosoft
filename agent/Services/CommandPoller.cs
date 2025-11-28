using Mudosoft.Agent.Models;
using Mudosoft.Shared.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Net.Http.Json;
using Mudosoft.Agent.Interfaces; // ⬅️ YENİ USING DİREKTİFİ
using System.Collections.Generic;
using System.Threading;

namespace Mudosoft.Agent.Services;

public sealed class CommandPoller : ICommandPoller
{
    private readonly HttpClient _http;
    private readonly AgentConfig _config;
    private readonly ILogger<CommandPoller> _logger;
    private readonly ICommandExecutor _executor;
    private readonly IDeviceIdentityProvider _identityProvider; 

    public CommandPoller(
        IHttpClientFactory httpFactory,
        IOptions<AgentConfig> options,
        ICommandExecutor executor,
        ILogger<CommandPoller> logger,
        IDeviceIdentityProvider identityProvider) 
    {
        _http = httpFactory.CreateClient();
        _config = options.Value;
        _executor = executor;
        _logger = logger;
        _identityProvider = identityProvider; 

        if (!string.IsNullOrWhiteSpace(_config.BackendUrl))
            _http.BaseAddress = new Uri(_config.BackendUrl);
    }

    public async Task PollAndExecuteAsync(CancellationToken token)
    {
        try
        {
            // 🏆 KRİTİK DÜZELTME: DeviceId artık IdentityProvider'dan geliyor
            var deviceId = _identityProvider.GetDeviceId();
            
            var url = $"api/agent/commands?deviceId={deviceId}";
            var commands = await _http.GetFromJsonAsync<List<CommandDto>>(url, token);
            
            if (commands is null || commands.Count == 0)
                return;

            foreach (var cmd in commands)
            {
                // Komutun alındığını logla
                _logger.LogInformation("➡️ Received command {CommandId} → {Type}", cmd.Id, cmd.Type);

                // Komutu yürüt
                var result = await _executor.ExecuteAsync(cmd, token);

                // Sonucu Backend'e geri gönder
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