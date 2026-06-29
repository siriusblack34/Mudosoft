using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models;

public class OnCallWorkday
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [StringLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    // Tarih UTC gece yarısı olarak saklanır (saat bilgisi yok)
    public DateTime WorkDate { get; set; }

    // ResmiTatil, HaftaSonu, Mesai (normal mesai günü fazla çalışma)
    [Required]
    [StringLength(30)]
    public string DayType { get; set; } = "ResmiTatil";

    [StringLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
