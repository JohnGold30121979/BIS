using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class ReportDataSet
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(160)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(300)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        public string SqlText { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public bool IsSystem { get; set; }

        public Guid? MetadataObjectId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<ReportDataSetField> Fields { get; set; } = new List<ReportDataSetField>();

        [NotMapped]
        public string StatusDisplay => IsActive ? "Активен" : "Отключен";
    }

    public class ReportDataSetField
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReportDataSetId { get; set; }

        [ForeignKey("ReportDataSetId")]
        public virtual ReportDataSet? ReportDataSet { get; set; }

        [Required]
        [MaxLength(160)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(160)]
        public string DbColumnName { get; set; } = string.Empty;

        [Required]
        [MaxLength(40)]
        public string FieldType { get; set; } = "String";

        public int Order { get; set; }
    }
}