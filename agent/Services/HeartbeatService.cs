// siriusblack34/mudosoft/Mudosoft-138a269b679ef64544ce6a0b899393e338ef513e/agent/Services/HeartbeatService.cs

using System.Net.Http.Json; 
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Shared.Dtos;
using Orchestra.Agent.Models;
using Orchestra.Agent.Interfaces;
using System.Text.Json; 
using System.Net.Http;
using System.Text; 
using System.Reflection; 
using System; 
using System.Threading; 
using System.Threading.Tasks;

namespace Orchestra.Agent.Services;

public sealed class HeartbeatService : IHeartbeatSender
{
    private readonly HttpClient _http;
    private readonly AgentConfig _config;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly ISystemInfoService _sys;
    private readonly IRsaKeyService _rsaKeys; 
    private readonly IAesEncryptionService _aes; 
    private readonly IDeviceIdentityProvider _identityProvider; 

    // Status properties for tray app
    public DateTime LastHeartbeatUtc { get; private set; } = DateTime.MinValue;
    public bool IsConnected { get; private set; } = false; 

    public HeartbeatService(
        IHttpClientFactory httpFactory,
        IOptions<AgentConfig> config,
        ILogger<HeartbeatService> logger,
        ISystemInfoService sys,
        IRsaKeyService rsaKeys, 
        IAesEncryptionService aes,
        IDeviceIdentityProvider identityProvider)
    {
        _http = httpFactory.CreateClient();
        _config = config.Value;
        _logger = logger;
        _sys = sys;
        _rsaKeys = rsaKeys;
        _aes = aes;
        _identityProvider = identityProvider;

        if (!string.IsNullOrWhiteSpace(_config.BackendUrl))
            _http.BaseAddress = new Uri(_config.BackendUrl);

        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task SendHeartbeatAsync(CancellationToken token)
    {
        const int maxAttempts = 2;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // 1. RSA Public Key'i çek
                var publicKey = await _rsaKeys.GetPublicKeyAsync(token);

                var ip = _config.IpAddress;
                if (string.IsNullOrWhiteSpace(ip))
                    ip = GetLocalIp();

                var agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
                var storeCode = _config.StoreCode ?? "0";

                // 2. Payload oluştur
                var payloadDto = new DeviceHeartbeatDto
                {
                    DeviceId = _identityProvider.GetDeviceId(),
                    Hostname = Environment.MachineName,
                    IpAddress = ip,

                    // Status
                    Online = true,

                    // Performance Metrics
                    CpuUsage = _sys.GetCpuUsage(),
                    RamUsage = _sys.GetRamUsage(),
                    DiskUsage = _sys.GetDiskUsage(),

                    // System Info
                    OsVersion = _sys.GetOsName(),
                    PosVersion = "",
                    SqlVersion = "",

                    // Hardware Inventory
                    CpuModel = _sys.GetCpuModel(),
                    TotalRamMB = _sys.GetTotalRamMB(),
                    TotalDiskGB = _sys.GetTotalDiskGB(),
                    GpuModel = _sys.GetGpuModel(),

                    // D Drive Metrics
                    DiskDUsage = _sys.GetDiskDUsage(),
                    TotalDiskDGB = _sys.GetTotalDiskDGB(),

                    // User & Session
                    LastLoggedInUser = _sys.GetLastLoggedInUser(),

                    // Uptime (actual boot time)
                    UptimeSince = _sys.GetSystemBootTime(),

                    // Agent Info
                    AgentVersion = agentVersion,
                    StoreCode = storeCode
                };

                // 3. Payload'u Şifrele (Hibrit Model)
                var encryptedPayload = _aes.EncryptPayload(payloadDto, publicKey);

                // 4. HTTP İsteğini Hazırla
                var request = new HttpRequestMessage(HttpMethod.Post, "api/agent/report");
                request.Headers.Add("X-Encrypted", "1");

                request.Content = new StringContent(
                    JsonSerializer.Serialize(encryptedPayload),
                    Encoding.UTF8,
                    "application/json");

                var resp = await _http.SendAsync(request, token);

                if (resp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("💓 Heartbeat OK → {Code} (attempt {Attempt})", resp.StatusCode, attempt);
                    LastHeartbeatUtc = DateTime.UtcNow;
                    IsConnected = true;
                    return; // Basarili — donguden cik
                }
                else
                {
                    _logger.LogWarning("💔 Heartbeat FAILED → {Code} (attempt {Attempt})", resp.StatusCode, attempt);
                }
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Heartbeat attempt {Attempt} failed, retrying in 3s...", attempt);
                await Task.Delay(3000, token);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Heartbeat ERROR (final attempt)");
                IsConnected = false;
                return;
            }
        }

        // Tum denemeler basarisiz
        IsConnected = false;
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