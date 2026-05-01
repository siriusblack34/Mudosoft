using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestra.Agent.Interfaces;
using Orchestra.Agent.Models;
using System;

namespace Orchestra.Agent.Services
{
    // Arayüz tanımınızın olası yapısını varsayarak oluşturulmuştur.
    public interface IRsaKeyService // (Projenizde bu zaten tanımlı olmalıdır)
    {
        Task<string> GetPublicKeyAsync(CancellationToken token);
    }
    
    public sealed class RsaKeyService : IRsaKeyService
    {
        private readonly HttpClient _http;
        private readonly AgentConfig _config;
        private readonly ILogger<RsaKeyService> _logger;
        private string? _publicKeyCache;

        public RsaKeyService(
            IHttpClientFactory httpFactory,
            IOptions<AgentConfig> config,
            ILogger<RsaKeyService> logger)
        {
            _http = httpFactory.CreateClient();
            _config = config.Value;
            _logger = logger;
            
            if (!string.IsNullOrWhiteSpace(_config.BackendUrl))
                _http.BaseAddress = new Uri(_config.BackendUrl);
        }

        public async Task<string> GetPublicKeyAsync(CancellationToken token)
        {
            if (_publicKeyCache != null)
            {
                return _publicKeyCache;
            }

            // 🔥 DÜZELTME: Endpoint yolunu Backend'deki [Route("api/[controller]")] ile eşleştiriyoruz.
            const string endpoint = "api/Security/public-key"; 

            try
            {
                _logger.LogInformation("RSA Public Key çekiliyor: {Endpoint}", endpoint);
                
                var response = await _http.GetStringAsync(endpoint, token);
                
                if (string.IsNullOrWhiteSpace(response))
                {
                    throw new Exception("Backend'den boş RSA Public Key alındı.");
                }

                _publicKeyCache = response.Trim(); 
                _logger.LogInformation("RSA Public Key başarıyla çekildi.");
                return _publicKeyCache;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ KRİTİK HATA: Backend'den RSA Public Key çekilemedi. Agent şifreleme yapamaz.");
                throw;
            }
        }
    }
}