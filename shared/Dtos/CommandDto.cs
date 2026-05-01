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
}