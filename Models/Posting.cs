using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class Posting
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid DocumentId { get; set; }

        [MaxLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        [MaxLength(50)]
        public string DocumentNumber { get; set; } = string.Empty;

        public DateTime Date { get; set; }

        [MaxLength(20)]
        public string DebitAccount { get; set; } = string.Empty;

        [MaxLength(20)]
        public string CreditAccount { get; set; } = string.Empty;

        [Column(TypeName = "numeric(15,2)")]
        public decimal Amount { get; set; }

        [Column(TypeName = "numeric(15,2)")]
        public decimal AmountCurrency { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "KGS";

        // Внешние ключи
        public Guid? OrganizationId { get; set; }
        public Guid? CounterpartyId { get; set; }
        public Guid? EmployeeId { get; set; }
        public Guid? MaterialId { get; set; }

        [MaxLength(500)]
        public string Note { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационные свойства
        [ForeignKey("OrganizationId")]
        public virtual Organization? Organization { get; set; }

        [ForeignKey("CounterpartyId")]
        public virtual Counterparty? Counterparty { get; set; }

        [ForeignKey("EmployeeId")]
        public virtual Employee? Employee { get; set; }
    }
}