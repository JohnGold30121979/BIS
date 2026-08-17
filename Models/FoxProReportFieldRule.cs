using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class FoxProReportFieldRule
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(120)] public string ProfileCode { get; set; } = string.Empty;
        [MaxLength(300)] public string SourcePattern { get; set; } = string.Empty;
        [MaxLength(120)] public string CanonicalField { get; set; } = string.Empty;
        [MaxLength(300)] public string TargetFieldName { get; set; } = string.Empty;
        [MaxLength(300)] public string TargetDisplayName { get; set; } = string.Empty;
        public bool IsRegex { get; set; }
        public bool IsActive { get; set; } = true;
        public int Priority { get; set; } = 100;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [NotMapped]
        public string DisplayName => string.IsNullOrWhiteSpace(TargetDisplayName)
            ? TargetFieldName
            : TargetDisplayName;
    }
}
