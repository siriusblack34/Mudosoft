namespace Mudosoft.Shared.Dtos;

public sealed class CommandResultDto
{
    public Guid CommandId { get; set; }
    public string DeviceId { get; set; } = default!;
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutedAtUtc { get; set; }
}