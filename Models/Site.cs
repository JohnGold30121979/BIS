using System;
using System.ComponentModel.DataAnnotations;

namespace BIS.ERP.Models
{   
    public class Site
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(20)]
        public string Code { get; set; } = string.Empty; // Код участка (1, 2, 3...)

        [MaxLength(200)]
        public string Name { get; set; } = string.Empty; // Наименование участка

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty; // Описание

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}