using System;
using System.ComponentModel.DataAnnotations;

namespace BIS.ERP.Models
{
    public class SystemConfiguration
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        [MaxLength(120)] public string SystemName { get; set; } = "BIS ERP";
        [MaxLength(20)] public string Icon { get; set; } = "🏢";
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
