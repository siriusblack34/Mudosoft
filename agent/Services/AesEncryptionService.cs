using System.Security.Cryptography;
using System.Text;
using Mudosoft.Shared.Dtos;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Mudosoft.Agent.Services;

public interface IAesEncryptionService
{
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
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var aesKeyBytes = aes.Key;
        var aesIVBytes = aes.IV;

        var aesKeyJson = JsonSerializer.Serialize(new
        {
            Key = Convert.ToBase64String(aesKeyBytes),
            IV = Convert.ToBase64String(aesIVBytes)
        });

        string encryptedAesKey;
        using (var rsa = RSA.Create())
        {
            rsa.FromXmlString(rsaPublicKey);
            var encryptedKeyBytes = rsa.Encrypt(
                Encoding.UTF8.GetBytes(aesKeyJson),
                RSAEncryptionPadding.OaepSHA256
            );
            encryptedAesKey = Convert.ToBase64String(encryptedKeyBytes);
        }

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

        using var encryptor = aesAlg.CreateEncryptor();
        using var msEncrypt = new MemoryStream();
        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        using (var swEncrypt = new StreamWriter(csEncrypt))
        {
            swEncrypt.Write(plainText);
        } // StreamWriter ve CryptoStream burada dispose edilir ve flush işlemi gerçekleşir.
        
        // DİKKAT: ToArray() dispose işleminden SONRA çağrılmalı, yoksa veri eksik olabilir.
        return Convert.ToBase64String(msEncrypt.ToArray());
    }
}
