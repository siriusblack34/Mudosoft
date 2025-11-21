using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mudosoft.Agent.Models;

namespace Mudosoft.Agent.Services;

public interface IRsaKeyService
{
    Task<string> GetPublicKeyAsync(CancellationToken token);
}

// Bu servis, Backend'den RSA Public Key'i çeker
public sealed class RsaKeyService : IRsaKeyService
{
    private readonly HttpClient _http;
    private readonly ILogger<RsaKeyService> _logger;
    private string _cachedPublicKey = string.Empty;

    public RsaKeyService(IHttpClientFactory httpFactory, IOptions<AgentConfig> config, ILogger<RsaKeyService> logger)
    {
        _http = httpFactory.CreateClient();
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(config.Value.BackendUrl))
            _http.BaseAddress = new Uri(config.Value.BackendUrl);
    }

    public async Task<string> GetPublicKeyAsync(CancellationToken token)
    {
        // Cache'te varsa hemen döndür
        if (!string.IsNullOrWhiteSpace(_cachedPublicKey))
            return _cachedPublicKey;

        try
        {
            // Backend'deki SecurityController'dan Public Key'i çeker
            var response = await _http.GetAsync("api/security/public-key", token); 
            response.EnsureSuccessStatusCode();

            _cachedPublicKey = await response.Content.ReadAsStringAsync(token);
            _logger.LogInformation("RSA Public Key başarıyla çekildi.");
            return _cachedPublicKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RSA Public Key çekilirken HATA oluştu.");
            throw;
        }
    }
}