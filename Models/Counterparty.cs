using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    /// <summary>
    /// Контрагент (клиент, поставщик)
    /// </summary>
    public class Counterparty
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(20)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Type { get; set; } = "Customer"; // Customer, Supplier, Both

        [MaxLength(50)]
        public string TaxId { get; set; } = string.Empty; // ИНН

        [MaxLength(50)]
        public string RegistrationNumber { get; set; } = string.Empty; // КПП

        [MaxLength(500)]
        public string Address { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(200)]
        public string ContactPerson { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}