using System.Security.Cryptography;
using System.Text;
using Mudosoft.Shared.Dtos;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Mudosoft.Agent.Services;

public interface IAesEncryptionService
{
    // Payload'u AES ile şifreler, AES anahtarını RSA ile şifreler
    EncryptedPayloadDto EncryptPayload(object payloadObject, string rsaPublicKey);
}

public sealed class AesEncryptionService : IAesEncryptionService
{
    private readonly ILogger<AesEncryptionService> _logger;

    public AesEncryptionService(ILogger<AesEncryptionService> logger)
    {
        _logger = logger;
    }

    public EncryptedPayloadDto EncryptPayload(object payloadObject, string rsaPublicKey)
    {
        // 1. Rastgele AES Anahtarı ve IV oluştur
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC; // Backend'deki AesEncryption.cs ile uyumlu olmalı
        aes.Padding = PaddingMode.PKCS7;

        var aesKeyBytes = aes.Key;
        var aesIVBytes = aes.IV;

        // AES Key ve IV'yi Backend'e göndermek için JSON formatına getir
        var aesKeyJson = JsonSerializer.Serialize(new { Key = Convert.ToBase64String(aesKeyBytes), IV = Convert.ToBase64String(aesIVBytes) });


        // 2. RSA ile AES Anahtarını Şifrele
        string encryptedAesKey;
        try
        {
            using var rsa = RSA.Create();
            rsa.FromXmlString(rsaPublicKey); // Public Key'i yükle
            
            var keyToEncrypt = Encoding.UTF8.GetBytes(aesKeyJson);
            // RSA ile şifreleme (OAEP önerilir, ancak FromXmlString kullandığı için yalnızca standart RSA şifrelemesini kullanabiliriz)
            var encryptedKeyBytes = rsa.Encrypt(keyToEncrypt, RSAEncryptionPadding.OaepSHA256); 
            encryptedAesKey = Convert.ToBase64String(encryptedKeyBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AES Anahtarı RSA ile şifrelenirken kritik hata oluştu.");
            throw;
        }

        // 3. Payload'u AES ile Şifrele
        var payloadJson = JsonSerializer.Serialize(payloadObject);
        var encryptedPayload = EncryptStringAes(payloadJson, aesKeyBytes, aesIVBytes);

        return new EncryptedPayloadDto
        {
            EncryptedAesKey = encryptedAesKey,
            EncryptedPayload = encryptedPayload
        };
    }
    
    private string EncryptStringAes(string plainText, byte[] key, byte[] iv)
    {
        using var aesAlg = Aes.Create();
        aesAlg.Key = key;
        aesAlg.IV = iv;
        aesAlg.Mode = CipherMode.CBC;
        aesAlg.Padding = PaddingMode.PKCS7;

        var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

        using var msEncrypt = new MemoryStream();
        using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
        using (var swEncrypt = new StreamWriter(csEncrypt))
        {
            swEncrypt.Write(plainText);
        }

        return Convert.ToBase64String(msEncrypt.ToArray());
    }
}