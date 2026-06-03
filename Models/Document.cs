using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class Document
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(50)]
        public string Number { get; set; } = string.Empty; // Номер документа

        [Required]
        public DateTime Date { get; set; } = DateTime.Now;

        [MaxLength(20)]
        public string DocumentType { get; set; } = "Operation"; // Operation, Invoice, Payment

        [MaxLength(200)]
        public string Description { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public Guid? DebitAccountId { get; set; } // Счет дебета
        public Guid? CreditAccountId { get; set; } // Счет кредита

        [MaxLength(100)]
        public string KontragentCode { get; set; } = string.Empty; // Код контрагента

        [MaxLength(200)]
        public string KontragentName { get; set; } = string.Empty; // Наименование контрагента

        [MaxLength(50)]
        public string OperationCode { get; set; } = string.Empty; // Код операции (KOD_STR)

        [MaxLength(500)]
        public string OperationDescription { get; set; } = string.Empty; // Описание операции (NAME_STR)

        public Guid? InfoBaseId { get; set; }

        [ForeignKey("InfoBaseId")]
        public virtual InfoBase? InfoBase { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public bool IsPosted { get; set; } = false; // Проведен
        public bool IsDeleted { get; set; } = false;
    }

    public class DocumentMovement
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid DocumentId { get; set; }

        [ForeignKey("DocumentId")]
        public virtual Document Document { get; set; }

        public Guid? AccountId { get; set; } // Счет
        public decimal Debit { get; set; } // Дебет
        public decimal Credit { get; set; } // Кредит
        public decimal Amount { get; set; }

        public DateTime MovementDate { get; set; } = DateTime.Now;
    }
}