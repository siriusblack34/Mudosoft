using System.Net.Http.Json; 
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Shared.Dtos;
using Mudosoft.Agent.Models;
using Mudosoft.Agent.Interfaces;
using System.Text.Json; 
using System.Net.Http;
using System.Text; // 🔥 HATA ÇÖZÜMÜ: Encoding için eklendi

namespace Mudosoft.Agent.Services;

public sealed class HeartbeatService : IHeartbeatSender
{
    private readonly HttpClient _http;
    private readonly AgentConfig _config;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly ISystemInfoService _sys;
    private readonly IRsaKeyService _rsaKeys; 
    private readonly IAesEncryptionService _aes; 

    public HeartbeatService(
        IHttpClientFactory httpFactory,
        IOptions<AgentConfig> config,
        ILogger<HeartbeatService> logger,
        ISystemInfoService sys,
        IRsaKeyService rsaKeys, 
        IAesEncryptionService aes) 
    {
        _http = httpFactory.CreateClient();
        _config = config.Value;
        _logger = logger;
        _sys = sys;
        _rsaKeys = rsaKeys;
        _aes = aes;

        if (!string.IsNullOrWhiteSpace(_config.BackendUrl))
            _http.BaseAddress = new Uri(_config.BackendUrl);
    }

    public async Task SendHeartbeatAsync(CancellationToken token)
    {
        try
        {
            // 1. RSA Public Key'i çek
            var publicKey = await _rsaKeys.GetPublicKeyAsync(token);

            var ip = _config.IpAddress;
            if (string.IsNullOrWhiteSpace(ip))
                ip = GetLocalIp(); // 🔥 HATA ÇÖZÜMÜ: Metod artık var.

            // 2. Payload oluştur
            var payloadDto = new DeviceHeartbeatDto
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

            // 3. Payload'u Şifrele (Hibrit Model)
            var encryptedPayload = _aes.EncryptPayload(payloadDto, publicKey);
            
            // 4. HTTP İsteğini Hazırla (X-Encrypted Header'ı ekle)
            // 🔥 HATA ÇÖZÜMÜ: StringContent constructor'ı Encoding ile düzeltildi.
            var content = new StringContent(
                JsonSerializer.Serialize(encryptedPayload), 
                Encoding.UTF8, 
                "application/json"); 
                
            content.Headers.Add("X-Encrypted", "1"); 

            var resp = await _http.PostAsync("api/agent/heartbeat", content, token); 

            if (resp.IsSuccessStatusCode)
                _logger.LogInformation("💓 Heartbeat OK → {Code}", resp.StatusCode);
            else
                _logger.LogWarning("💔 Heartbeat FAILED → {Code} - {Body}", resp.StatusCode, await resp.Content.ReadAsStringAsync(token));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat ERROR");
        }
    }
    
    // 🔥 HATA ÇÖZÜMÜ: Kayıp olan GetLocalIp metodu geri eklendi.
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