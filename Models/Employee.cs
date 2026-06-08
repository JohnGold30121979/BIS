using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class Employee
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(20)]
        public string PersonnelNumber { get; set; } = string.Empty;

        [MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Position { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Department { get; set; } = string.Empty;

        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Активен";

        [MaxLength(50)]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(50)]
        public string TaxId { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}