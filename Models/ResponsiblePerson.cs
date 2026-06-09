using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{  
    public class ResponsiblePerson
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(20)]
        public string PersonnelNumber { get; set; } = string.Empty; // Табельный номер

        [MaxLength(200)]
        public string FullName { get; set; } = string.Empty; // ФИО МОЛ

        // Связь с участком (один МОЛ может быть закреплен за одним участком)
        public Guid? SiteId { get; set; }

        [MaxLength(500)]
        public string Note { get; set; } = string.Empty; // Примечание

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        [ForeignKey("SiteId")]
        public virtual Site? Site { get; set; }
    }
}