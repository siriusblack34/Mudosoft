using System;
using Orchestra.Shared.Enums;

namespace Orchestra.Shared.Dtos;

public sealed class CommandDto
{
    public Guid Id { get; set; }
    public string DeviceId { get; set; } = default!;
    public CommandType Type { get; set; }

    // Payload: ExecuteScript komutu için çalıştırılacak betik (örneğin PowerShell kodu)
    public string? Payload { get; set; } 
    
    public DateTime CreatedAtUtc { get; set; }

    // 🔒 Faz 2 (K-2) — komut imzalama. ADDITIVE: eski agent'lar bu alanları yok sayar (System.Text.Json
    // bilinmeyen/null alanları görmezden gelir). Stage-1 agent'ı backend public key ile doğrular:
    //   imza = RSA-SHA256(PKCS1) over "{Id}|{DeviceId}|{(int)Type}|{Payload}|{Seq}|{IssuedAtUtc:O}|{ExpiresAtUtc:O}"
    // ve ayrıca DeviceId==self, Seq>sonGörülen (replay), now∈[IssuedAt-skew, ExpiresAt] kontrol eder.
    public long? Seq { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? Signature { get; set; }
}