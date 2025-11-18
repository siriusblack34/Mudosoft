using System.Security.Cryptography;
using System.Text;

namespace MudoSoft.Backend.Crypto;

public class RsaKeyProvider
{
    private static RSA? _privateKey;
    private static RSA? _publicKey;

    public RsaKeyProvider(IHostEnvironment env)
    {
        var keyPath = Path.Combine(env.ContentRootPath, "rsa_private.xml");

        if (File.Exists(keyPath))
        {
            var xml = File.ReadAllText(keyPath);
            _privateKey = RSA.Create();
            _privateKey.FromXmlString(xml);

            _publicKey = RSA.Create();
            _publicKey.FromXmlString(xml); // public key buradan çıkar
        }
        else
        {
            _privateKey = RSA.Create(2048);
            var xml = _privateKey.ToXmlString(true);
            File.WriteAllText(keyPath, xml);

            _publicKey = RSA.Create();
            _publicKey.FromXmlString(xml);
        }
    }

    public string GetPublicKey()
    {
        return _publicKey!.ToXmlString(false);
    }

    public byte[] Decrypt(byte[] encrypted)
    {
        return _privateKey!.Decrypt(encrypted, RSAEncryptionPadding.Pkcs1);
    }
}