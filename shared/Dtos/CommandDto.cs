using Mudosoft.Shared.Enums;

namespace Mudosoft.Shared.Dtos;

public class CommandDto
{
    // Backend tarafından atanacak benzersiz GUID
    public Guid Id { get; set; }

    // Hedef cihaz (agentId)
    public string DeviceId { get; set; } = "";

    // Komut tipi
    public CommandType Type { get; set; }

    // Komut parametresi / payload
    public string Command { get; set; } = "";

    // Komut oluşturulma zamanı
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
