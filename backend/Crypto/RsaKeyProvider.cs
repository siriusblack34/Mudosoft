using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using System.IO;

namespace Orchestra.Backend.Crypto;

public class RsaKeyProvider
{
    private readonly RSA _rsa; // 🔥 HATA ÇÖZÜMÜ: Sınıf seviyesinde tanımlandı.
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private const string RsaKeyFileName = "rsa_private.xml";    // legacy: düz-metin XML
    private const string RsaKeyEncFileName = "rsa_private.dat"; // 🔒 DPAPI ile şifreli

    // 🔒 SECURITY: DPAPI entropy — şifreli dosyayı çözmek için makine DPAPI anahtarı + bu entropy gerekir.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Orchestra.Rsa.KeyStore.v1");

    public RsaKeyProvider(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
        _rsa = RSA.Create();

        LoadOrCreateKey();
    }

    private void LoadOrCreateKey()
    {
        var legacyPath = Path.Combine(_env.ContentRootPath, RsaKeyFileName);
        var encPath = Path.Combine(_env.ContentRootPath, RsaKeyEncFileName);

        // 1) Şifreli dosya varsa (normal durum, Windows): onu çöz ve yükle.
        if (OperatingSystem.IsWindows() && File.Exists(encPath))
        {
            var enc = File.ReadAllBytes(encPath);
            var xml = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.LocalMachine));
            _rsa.FromXmlString(xml);
            return;
        }

        // 2) Eski düz-metin anahtar varsa: aynı anahtarı yükle, (Windows'ta) şifreli olarak yeniden yaz,
        //    düz-metin dosyayı sil. Anahtar DEĞERİ değişmez → agent'ların cache'lediği public key aynı kalır.
        if (File.Exists(legacyPath))
        {
            var xml = File.ReadAllText(legacyPath);
            _rsa.FromXmlString(xml);
            if (OperatingSystem.IsWindows())
            {
                SaveEncrypted(encPath, xml);
                try { File.Delete(legacyPath); } catch { /* ACL/lock — kalsın, kritik değil */ }
            }
            return;
        }

        // 3) Hiç anahtar yoksa: yeni 2048-bit üret ve şifreli (Windows) ya da düz-metin (dev/non-Windows) sakla.
        _rsa.KeySize = 2048;
        var newXml = _rsa.ToXmlString(true);
        if (OperatingSystem.IsWindows())
            SaveEncrypted(encPath, newXml);
        else
            File.WriteAllText(legacyPath, newXml); // dev fallback (DPAPI yalnızca Windows)
    }

    private static void SaveEncrypted(string path, string xml)
    {
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(xml), Entropy, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(path, enc);
    }

    public byte[] Decrypt(byte[] cipherText)
    {
        // AES key'i çözerken OAEP kullanıldığı varsayılır (Agent'ta kullandığımız gibi)
        return _rsa.Decrypt(cipherText, RSAEncryptionPadding.OaepSHA256);
    }

    // 🔒 Faz 2 (K-2): Backend komutları/manifest'i bu özel anahtarla imzalar.
    // Agent, /api/Security/public-key'den aldığı public key ile RSA-SHA256 (PKCS1) doğrular.
    public byte[] Sign(byte[] data)
    {
        return _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public string GetPublicKeyString()
    {
        return _rsa.ToXmlString(false);
    }
}
