using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using System.IO;

namespace MudoSoft.Backend.Crypto;

public class RsaKeyProvider
{
    private readonly RSA _rsa; // 🔥 HATA ÇÖZÜMÜ: Sınıf seviyesinde tanımlandı.
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private const string RsaKeyFileName = "rsa_private.xml";

    public RsaKeyProvider(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
        _rsa = RSA.Create();

        LoadOrCreateKey();
    }

    private void LoadOrCreateKey()
    {
        var keyPath = Path.Combine(_env.ContentRootPath, RsaKeyFileName);

        if (File.Exists(keyPath))
        {
            var xmlString = File.ReadAllText(keyPath);
            _rsa.FromXmlString(xmlString);
        }
        else
        {
            _rsa.KeySize = 2048;
            var xmlString = _rsa.ToXmlString(true);
            File.WriteAllText(keyPath, xmlString);
        }
    }

    public byte[] Decrypt(byte[] cipherText)
    {
        // AES key'i çözerken OAEP kullanıldığı varsayılır (Agent'ta kullandığımız gibi)
        return _rsa.Decrypt(cipherText, RSAEncryptionPadding.OaepSHA256);
    }

    public string GetPublicKeyString()
    {
        // CS0103 hatası çözüldü
        return _rsa.ToXmlString(false); 
    }
}