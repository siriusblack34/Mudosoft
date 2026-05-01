using System;
using System.ComponentModel.DataAnnotations;

namespace Orchestra.Backend.Models
{
    public class Note
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(50)]
        public string OwnerUsername { get; set; } = "";

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = "";

        public string Content { get; set; } = "";

        public bool IsShared { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
