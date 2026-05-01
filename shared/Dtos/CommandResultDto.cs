using System;
using Orchestra.Shared.Enums;

namespace Orchestra.Shared.Dtos; // Namespace eklendi (Gerekiyorsa)

public class CommandResultDto
{
    public Guid CommandId { get; set; }
    public string DeviceId { get; set; } = "";
    public CommandType CommandType { get; set; } // HATA ÇÖZÜMÜ: Eklendi
    public bool Success { get; set; }
    public string Output { get; set; } = "";
}