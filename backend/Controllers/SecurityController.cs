using Microsoft.AspNetCore.Mvc;
using MudoSoft.Backend.Crypto;

namespace MudoSoft.Backend.Controllers;

[ApiController]
[Route("api/[controller]")] // Endpoint: api/security
public class SecurityController : ControllerBase
{
    private readonly RsaKeyProvider _rsaProvider;

    public SecurityController(RsaKeyProvider rsaProvider)
    {
        _rsaProvider = rsaProvider;
    }

    /// <summary>
    /// Agent'ların veri yükünü AES ile şifrelemeden önce, AES anahtarını şifrelemek için 
    /// kullanacağı RSA Public Key'i (XML formatında) döndürür.
    /// </summary>
    /// <returns>RSA Public Key (XML formatında string)</returns>
    [HttpGet("public-key")]
    public IActionResult GetPublicKey()
    {
        // RsaKeyProvider.cs'de RSA nesnesinin zaten yüklü olduğunu varsayarak
        // Public Key'i XML string olarak döndürür.
        var publicKey = _rsaProvider.GetPublicKeyString();
        
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            // Eğer rsa_private.xml dosyası yoksa veya key yüklenemezse hata döndür
            return StatusCode(500, "RSA Public Key could not be loaded or generated.");
        }

        // Public Key, Agent'ın kolayca okuyabileceği sade metin olarak döndürülür.
        return Content(publicKey, "text/plain");
    }
}