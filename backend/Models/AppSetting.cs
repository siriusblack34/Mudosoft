using System.ComponentModel.DataAnnotations;

namespace MudoSoft.Backend.Models;

public class AppSetting
{
    [Key]
    [StringLength(100)]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
