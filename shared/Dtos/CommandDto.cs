using Mudosoft.Shared.Enums;

namespace Mudosoft.Shared.Dtos;

public sealed class CommandDto
{
    public Guid Id { get; set; }
    public string DeviceId { get; set; } = default!;
    public CommandType Type { get; set; }
    public string? ArgumentsJson { get; set; } // basit olsun diye string
    public DateTime CreatedAtUtc { get; set; }
}
