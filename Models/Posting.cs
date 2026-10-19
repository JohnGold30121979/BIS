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
        public Guid? EmployeeId { get; set; }
        public Guid? MaterialId { get; set; }

        [MaxLength(500)]
        public string Note { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационные свойства
        // LEGACY: Posting.Organization ведет в пустую статичную таблицу Organizations.
        // Боевой справочник — catalog_organizations (см. Docs/План_нормализации_модели_данных.md §3.1).
        // Новые ссылки запрещены; организацию читать из catalog_organizations по OrganizationId.
        [Obsolete("Legacy, только чтение. Боевой справочник — catalog_organizations.")]
        [ForeignKey("OrganizationId")]
        public virtual Organization? Organization { get; set; }

        // План §3.1: навигация на статичную Employee убрана — таблица пустая,
        // сотрудники живут в catalog_employees, а EmployeeId остаётся простым Guid.
    }
}
