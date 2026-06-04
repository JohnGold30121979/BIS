using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class Document
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(50)]
        public string Number { get; set; } = string.Empty;

        public DateTime Date { get; set; } = DateTime.UtcNow;

        [MaxLength(20)]
        public string DocumentType { get; set; } = "Operation";

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public decimal TotalAmount { get; set; } = 0;  // Добавляем поле для итоговой суммы

        [MaxLength(50)]
        public string OperationCode { get; set; } = string.Empty;

        [MaxLength(500)]
        public string OperationDescription { get; set; } = string.Empty;

        [MaxLength(100)]
        public string KontragentCode { get; set; } = string.Empty;

        [MaxLength(200)]
        public string KontragentName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public bool IsPosted { get; set; } = false;
        public bool IsDeleted { get; set; } = false;

        // Навигационное свойство для строк документа
        public virtual ICollection<DocumentRow> Rows { get; set; } = new List<DocumentRow>();
    }

    public class DocumentRow
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid DocumentId { get; set; }

        [ForeignKey("DocumentId")]
        public virtual Document Document { get; set; }

        public int LineNumber { get; set; }

        [MaxLength(50)]
        public string OperationCode { get; set; } = string.Empty;

        [MaxLength(500)]
        public string OperationDescription { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        [MaxLength(500)]
        public string Note { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class DocumentMovement
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid DocumentId { get; set; }

        [ForeignKey("DocumentId")]
        public virtual Document Document { get; set; }

        public Guid? AccountId { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Amount { get; set; }

        public DateTime MovementDate { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}