using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Mudosoft.Agent.Models;

namespace Mudosoft.Agent.Services;

/// <summary>
/// Named Pipe server for communication with Tray application
/// </summary>
public class PipeServer : IHostedService
{
    private readonly ILogger<PipeServer> _logger;
    private readonly IServiceProvider _services;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public PipeServer(ILogger<PipeServer> logger, IServiceProvider services)
    {
        _logger = logger;
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PipeServer starting...");
        _cts = new CancellationTokenSource();
        _serverTask = RunServerAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PipeServer stopping...");
        _cts?.Cancel();
        
        if (_serverTask != null)
        {
            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("PipeServer did not stop gracefully");
            }
        }
    }

    private async Task RunServerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Create pipe with security that allows all users to connect
                var pipeSecurity = new System.IO.Pipes.PipeSecurity();
                pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                    System.IO.Pipes.PipeAccessRights.ReadWrite,
                    System.Security.AccessControl.AccessControlType.Allow));

                using var server = NamedPipeServerStreamAcl.Create(
                    "MudoSoftAgentPipe",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0, 0,
                    pipeSecurity);

                _logger.LogDebug("Waiting for pipe connection...");
                
                try
                {
                    await server.WaitForConnectionAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _logger.LogDebug("Pipe client connected");

                // Read request
                var buffer = new byte[1024];
                var bytesRead = await server.ReadAsync(buffer, token);
                
                if (bytesRead > 0)
                {
                    var request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var response = await HandleRequestAsync(request);
                    
                    var responseBytes = Encoding.UTF8.GetBytes(response);
                    await server.WriteAsync(responseBytes, token);
                    await server.FlushAsync(token);
                }

                server.Disconnect();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PipeServer error");
                await Task.Delay(1000, token);
            }
        }
    }

    private Task<string> HandleRequestAsync(string request)
    {
        try
        {
            var cmd = JsonSerializer.Deserialize<PipeCommand>(request, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (cmd?.Command == "status")
            {
                // Get status info - use interfaces directly
                var identityProvider = _services.GetService<Mudosoft.Agent.Interfaces.IDeviceIdentityProvider>();
                var heartbeatService = _services.GetService<HeartbeatService>();

                var response = new
                {
                    version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?",
                    deviceId = identityProvider?.GetDeviceId() ?? "?",
                    lastHeartbeat = heartbeatService?.LastHeartbeatUtc ?? DateTime.MinValue,
                    isConnected = heartbeatService?.IsConnected ?? false
                };

                return Task.FromResult(JsonSerializer.Serialize(response));
            }

            return Task.FromResult(JsonSerializer.Serialize(new { error = "Unknown command" }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    private class PipeCommand
    {
        public string? Command { get; set; }
    }
}
