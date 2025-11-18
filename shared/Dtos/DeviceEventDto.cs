namespace Mudosoft.Shared.Dtos;

public sealed class DeviceEventDto
{
    public string DeviceId { get; set; } = default!;
    public string EventType { get; set; } = default!; // "pos_freeze", "disk_high" vs.
    public string Severity { get; set; } = "info";    // info|low|medium|high|critical
    public string? Details { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}