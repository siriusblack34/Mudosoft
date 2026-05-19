using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Role { get; set; } = "Teknisyen"; // Admin, Teknisyen

    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(150)]
    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsLdapUser { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public class LoginHistory
{
    [Key]
    public int Id { get; set; }

    public int? UserId { get; set; }

    [Required]
    [StringLength(50)]
    public string Username { get; set; } = string.Empty;

    public DateTime LoginAt { get; set; } = DateTime.UtcNow;

    [StringLength(45)]
    public string? IpAddress { get; set; }

    public bool Success { get; set; }
}
