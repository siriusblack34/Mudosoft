using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models;

public class RemediationPlaybook
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    // ServiceDown, DeviceOffline, CpuHigh, DiskFull, HealthScoreLow
    [Required]
    [StringLength(50)]
    public string TriggerType { get; set; } = string.Empty;

    // JSON: trigger için eşik değerleri, örn. {"threshold": 90, "serviceName": "Genius3"}
    [StringLength(1000)]
    public string? TriggerConditionJson { get; set; }

    public bool IsEnabled { get; set; } = true;

    [Required]
    [StringLength(100)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<PlaybookStep> Steps { get; set; } = [];
    public List<PlaybookExecution> Executions { get; set; } = [];
}

public class PlaybookStep
{
    [Key]
    public int Id { get; set; }

    public int PlaybookId { get; set; }
    public RemediationPlaybook Playbook { get; set; } = null!;

    public int StepOrder { get; set; }

    // RestartService, RunScript, SendAlert, Wait
    [Required]
    [StringLength(50)]
    public string ActionType { get; set; } = string.Empty;

    // JSON ile aksiyon parametreleri: {"serviceName":"Genius3"} veya {"script":"net stop..."}
    [StringLength(2000)]
    public string? ActionPayloadJson { get; set; }

    // Bu adım öncesi beklenecek saniye
    public int DelaySeconds { get; set; } = 0;

    [StringLength(200)]
    public string? Description { get; set; }
}

public class PlaybookExecution
{
    [Key]
    public int Id { get; set; }

    public int PlaybookId { get; set; }
    public RemediationPlaybook Playbook { get; set; } = null!;

    [StringLength(450)]
    public string? DeviceId { get; set; }

    [StringLength(100)]
    public string? Hostname { get; set; }

    [StringLength(50)]
    public string? StoreCode { get; set; }

    // Running, Success, Failed, Partial
    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "Running";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    [StringLength(2000)]
    public string? ResultSummary { get; set; }

    [Required]
    [StringLength(500)]
    public string TriggerReason { get; set; } = string.Empty;
}
