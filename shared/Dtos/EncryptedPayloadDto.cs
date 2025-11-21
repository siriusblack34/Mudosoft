using System;

namespace Mudosoft.Shared.Dtos;

// Agent'tan Backend'e gönderilecek yeni veri zarfı
public class EncryptedPayloadDto
{
    // RSA Public Key ile şifrelenmiş AES Key (Asimetrik)
    public string EncryptedAesKey { get; set; } = default!; 

    // AES Key ile şifrelenmiş asıl veri (JSON payload) (Simetrik)
    public string EncryptedPayload { get; set; } = default!;
}