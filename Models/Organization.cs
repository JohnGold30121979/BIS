using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    /// <summary>
    /// Организация (юридическое лицо)
    /// </summary>
    public class Organization
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(20)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string TaxId { get; set; } = string.Empty; // ИНН

        [MaxLength(50)]
        public string RegistrationNumber { get; set; } = string.Empty; // КПП

        [MaxLength(20)]
        public string Okpo { get; set; } = string.Empty; // ОКПО

        [MaxLength(500)]
        public string LegalAddress { get; set; } = string.Empty;

        [MaxLength(500)]
        public string ActualAddress { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Website { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Director { get; set; } = string.Empty;

        [MaxLength(200)]
        public string ChiefAccountant { get; set; } = string.Empty;

        [MaxLength(3)]
        public string BaseCurrency { get; set; } = "KGS";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}