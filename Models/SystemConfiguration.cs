using System;
using System.ComponentModel.DataAnnotations;

namespace BIS.ERP.Models
{
    public class SystemConfiguration
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        [MaxLength(120)] public string SystemName { get; set; } = "BIS ERP";
        [MaxLength(20)] public string Icon { get; set; } = "🏢";
        [MaxLength(1000)] public string Description { get; set; } = "Корпоративная информационная система";
        [MaxLength(2000)] public string CompanyDetails { get; set; } = string.Empty;
        [MaxLength(160)] public string Email { get; set; } = string.Empty;
        [MaxLength(80)] public string Phone { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
