using System.Security.Cryptography;
using System.Text;
using System.Text.Json; 

namespace MudoSoft.Backend.Crypto;

public class AesEncryption // 'static' anahtar kelimesi yok
{
    // FIX: Middleware'in ihtiyaç duyduğu 3 parametreli Decrypt metodu
    public string Decrypt(string cipherText, byte[] key, byte[] iv)
    {
        // 1. Base64'ten şifreli metni byte array'e dönüştür
        var cipherBytes = Convert.FromBase64String(cipherText);

        using var aesAlg = Aes.Create();
        aesAlg.Key = key;
        aesAlg.IV = iv;
        aesAlg.Mode = CipherMode.CBC;
        aesAlg.Padding = PaddingMode.PKCS7;

        var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

        using var msDecrypt = new MemoryStream(cipherBytes);
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var srDecrypt = new StreamReader(csDecrypt);
        
        return srDecrypt.ReadToEnd();
    }
}