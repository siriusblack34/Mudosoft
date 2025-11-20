using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Shared.Dtos;
using Mudosoft.Agent.Models;
using Mudosoft.Agent.Interfaces;


namespace Mudosoft.Agent.Services;

public sealed class HeartbeatService : IHeartbeatSender
{
    private readonly HttpClient _http;
    private readonly AgentConfig _config;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly ISystemInfoService _sys;

    public HeartbeatService(
        IHttpClientFactory httpFactory,
        IOptions<AgentConfig> config,
        ILogger<HeartbeatService> logger,
        ISystemInfoService sys)
    {
        _http = httpFactory.CreateClient();
        _config = config.Value;
        _logger = logger;
        _sys = sys;

        if (!string.IsNullOrWhiteSpace(_config.BackendUrl))
            _http.BaseAddress = new Uri(_config.BackendUrl);
    }

    public async Task SendHeartbeatAsync(CancellationToken token)
    {
        try
        {
            var ip = _config.IpAddress;
            if (string.IsNullOrWhiteSpace(ip))
                ip = GetLocalIp();

            var dto = new DeviceHeartbeatDto
            {
                DeviceId = _config.DeviceId,
                Hostname = Environment.MachineName,
                IpAddress = ip,
                Online = true,
                CpuUsage = _sys.GetCpuUsage(),
                RamUsage = _sys.GetRamUsage(),
                DiskUsage = _sys.GetDiskUsage(),
                OsVersion = Environment.OSVersion.ToString(),
                PosVersion = "",
                SqlVersion = "",
                UptimeSince = DateTime.UtcNow
            };

            var resp = await _http.PostAsJsonAsync("api/agent/heartbeat", dto, token);

            if (resp.IsSuccessStatusCode)
                _logger.LogInformation("💓 Heartbeat OK → {Code}", resp.StatusCode);
            else
                _logger.LogWarning("💔 Heartbeat FAILED → {Code}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat ERROR");
        }
    }

    private string GetLocalIp()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            var props = nic.GetIPProperties();
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    return addr.Address.ToString();
            }
        }
        return "0.0.0.0";
    }
}
