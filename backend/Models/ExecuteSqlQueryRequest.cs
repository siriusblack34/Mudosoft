// backend/Models/ExecuteSqlQueryRequest.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace MudoSoft.Backend.Models
{
    public class ExecuteSqlQueryRequest
    {
        [Required]
        public string DeviceId { get; set; } = string.Empty;

        [Required]
        [MinLength(5)]
        public string Query { get; set; } = string.Empty;
    }
}