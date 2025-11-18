namespace MudoSoft.Backend.Models;

public class EncryptedPayload
{
    public string EncryptedKey { get; set; } = default!; // RSA encrypted AES key
    public string IV { get; set; } = default!;           // base64
    public string Data { get; set; } = default!;         // base64 AES encrypted data
}
