using System.ComponentModel.DataAnnotations;

namespace MudoSoft.Backend.Models;

public class AgendaItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    public string Content { get; set; } = "";

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Takipte";

    [Required]
    [MaxLength(20)]
    public string Priority { get; set; } = "Orta";

    [Required]
    [MaxLength(40)]
    public string Category { get; set; } = "Duyuru";

    public DateTime? DueDate { get; set; }

    [Required]
    [MaxLength(100)]
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
