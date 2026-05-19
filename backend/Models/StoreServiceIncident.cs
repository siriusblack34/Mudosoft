using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models;

public class StoreServiceIncident
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string DeviceId { get; set; } = "";

    public int StoreCode { get; set; }

    [Required]
    [StringLength(100)]
    public string StoreName { get; set; } = "";

    [Required]
    [StringLength(50)]
    public string DeviceName { get; set; } = "";

    [Required]
    [StringLength(15)]
    public string IpAddress { get; set; } = "";

    [Required]
    [StringLength(120)]
    public string ServiceName { get; set; } = "";

    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = "";

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = "";

    [Required]
    [StringLength(20)]
    public string Severity { get; set; } = "Critical";

    [Required]
    [StringLength(500)]
    public string Message { get; set; } = "";

    [StringLength(40)]
    public string? LastStartMode { get; set; }

    [StringLength(1000)]
    public string? LastError { get; set; }

    public int ConsecutiveFailures { get; set; }
    public DateTime FirstDetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastDetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}
