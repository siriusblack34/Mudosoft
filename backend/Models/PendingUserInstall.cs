using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models;

public enum PendingUserInstallStatus
{
    Waiting,
    Matched,
    Installing,
    Done,
    Failed,
    Expired,
    Cancelled
}

public class PendingUserInstall
{
    [Key]
    public int Id { get; set; }

    [MaxLength(256)]
    public string SamAccountName { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? DisplayName { get; set; }

    [MaxLength(100)]
    public string? RequestedBy { get; set; }

    public DateTime RequestedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public PendingUserInstallStatus Status { get; set; } = PendingUserInstallStatus.Waiting;

    [MaxLength(256)]
    public string? MatchedComputer { get; set; }

    [MaxLength(64)]
    public string? MatchedIp { get; set; }

    public DateTime? MatchedAt { get; set; }

    [MaxLength(64)]
    public string? InstallId { get; set; }

    [MaxLength(1024)]
    public string? LastError { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public class DcLogCursor
{
    [Key]
    [MaxLength(256)]
    public string DcName { get; set; } = string.Empty;

    public long LastRecordId { get; set; }

    public DateTime UpdatedAt { get; set; }
}
