using System.ComponentModel.DataAnnotations;

namespace MudoSoft.Backend.Models;

public class VncSessionLog
{
    public long Id { get; set; }

    [MaxLength(64)]
    public string SessionId { get; set; } = "";

    [MaxLength(450)]
    public string DeviceId { get; set; } = "";

    [MaxLength(256)]
    public string Username { get; set; } = "";

    [MaxLength(64)]
    public string? TargetIp { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public int DurationSeconds { get; set; }

    [MaxLength(32)]
    public string? DisconnectReason { get; set; } // "clean", "error", "idle_timeout"
}
