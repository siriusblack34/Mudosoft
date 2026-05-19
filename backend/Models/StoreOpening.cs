using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Orchestra.Backend.Models;

public class StoreOpening
{
    [Key]
    public int Id { get; set; }

    public int StoreCode { get; set; }

    [Required, StringLength(150)]
    public string StoreName { get; set; } = string.Empty;

    [StringLength(80)]
    public string? City { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    public DateTime TargetOpeningDate { get; set; }

    public DateTime? ActualOpeningDate { get; set; }

    [Required, StringLength(20)]
    public string Status { get; set; } = "Planned"; // Planned, InProgress, Completed, Cancelled

    public int? TemplateId { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    /// <summary>JSON: { "RoleName": "username", ... }</summary>
    public string? RoleAssignmentsJson { get; set; }

    [StringLength(100)]
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [StringLength(100)]
    public string? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }

    public List<StoreOpeningItem> Items { get; set; } = new();
}

public class StoreOpeningItem
{
    [Key]
    public int Id { get; set; }

    public int StoreOpeningId { get; set; }
    [JsonIgnore]
    public StoreOpening? StoreOpening { get; set; }

    [Required, StringLength(100)]
    public string CategoryName { get; set; } = string.Empty;

    [StringLength(50)]
    public string? AssignedRole { get; set; }

    [Required, StringLength(200)]
    public string ItemName { get; set; } = string.Empty;

    /// <summary>Üst kalem adı (alt-kalemler için, ör. "Monitör" → "Güç Kablosu")</summary>
    [StringLength(200)]
    public string? ParentName { get; set; }

    public bool HasSerialNumber { get; set; }
    public bool HasAssetNumber { get; set; }

    [StringLength(100)]
    public string? SerialNumber { get; set; }

    [StringLength(100)]
    public string? AssetNumber { get; set; }

    [Required, StringLength(20)]
    public string Status { get; set; } = "Pending"; // Pending, Completed, NotApplicable

    [StringLength(500)]
    public string? PhotoUrl { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public int SortOrder { get; set; }

    [StringLength(100)]
    public string? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class StoreOpeningTemplate
{
    [Key]
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsDefault { get; set; }

    [StringLength(100)]
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<StoreOpeningTemplateItem> Items { get; set; } = new();
}

public class StoreOpeningTemplateItem
{
    [Key]
    public int Id { get; set; }

    public int TemplateId { get; set; }
    [JsonIgnore]
    public StoreOpeningTemplate? Template { get; set; }

    [Required, StringLength(100)]
    public string CategoryName { get; set; } = string.Empty;

    [StringLength(50)]
    public string? AssignedRole { get; set; }

    [Required, StringLength(200)]
    public string ItemName { get; set; } = string.Empty;

    [StringLength(200)]
    public string? ParentName { get; set; }

    public bool HasSerialNumber { get; set; }
    public bool HasAssetNumber { get; set; }

    public int SortOrder { get; set; }
}

public class StoreOpeningActivity
{
    [Key]
    public int Id { get; set; }

    public int StoreOpeningId { get; set; }

    [Required, StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Action { get; set; } = string.Empty; // ItemCompleted, ItemReopened, OpeningCreated, OpeningCompleted, NotesUpdated...

    [StringLength(500)]
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
