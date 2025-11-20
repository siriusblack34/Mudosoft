using System.Security.Cryptography;
using System.Text;

namespace MudoSoft.Backend.Crypto;

public class RsaKeyProvider
{
    private readonly RSA _privateKey;
    private readonly RSA _publicKey;

    public RsaKeyProvider()
    {
        // TEST amaçlı key üretimi
        _privateKey = RSA.Create();
        _publicKey = RSA.Create();

        var p = _privateKey.ExportParameters(true);
        _publicKey.ImportParameters(p);
    }

    // 🔥 Decrypt byte[] alır → string döner
    public string Decrypt(byte[] encrypted)
    {
        var decryptedBytes = _privateKey.Decrypt(
            encrypted,
            RSAEncryptionPadding.OaepSHA256
        );

        return Encoding.UTF8.GetString(decryptedBytes);
    }

    // İstersen agent için encrypt de ekliyoruz
    public byte[] Encrypt(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return _publicKey.Encrypt(bytes, RSAEncryptionPadding.OaepSHA256);
    }
}
