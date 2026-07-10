using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class AccountingPeriod
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        [MaxLength(20)] public string Status { get; set; } = "Open";
        public DateTime? CollectedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public bool IsLocked { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AccountingPeriodModuleState
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PeriodId { get; set; }
        public Guid ModuleId { get; set; }
        public bool IsClosed { get; set; }
        public DateTime? ClosedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AccountingPeriodModuleStatus
    {
        public Guid PeriodId { get; set; }
        public Guid ModuleId { get; set; }
        public string ModuleCode { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public int CloseOrder { get; set; }
        public bool ParticipatesInPeriodClose { get; set; }
        public bool RequirePreviousModulesClosed { get; set; }
        public bool IsClosed { get; set; }
        public DateTime? ClosedAt { get; set; }

        public string DisplayName => $"{CloseOrder:000} - {ModuleName}";
        public string StateCaption => IsClosed
            ? $"закрыт {ClosedAt:dd.MM.yyyy HH:mm}"
            : "открыт";
    }

    public class AccountOpeningBalance
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime BalanceDate { get; set; }
        [MaxLength(50)] public string AccountCode { get; set; } = string.Empty;
        [Column(TypeName = "numeric(18,2)")] public decimal Debit { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal Credit { get; set; }
        public Guid? SourcePeriodId { get; set; }
        public bool IsSystemGenerated { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AccountTurnoverSnapshot
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PeriodId { get; set; }
        [MaxLength(50)] public string AccountCode { get; set; } = string.Empty;
        [MaxLength(300)] public string AccountName { get; set; } = string.Empty;
        [Column(TypeName = "numeric(18,2)")] public decimal OpeningDebit { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal OpeningCredit { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal TurnoverDebit { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal TurnoverCredit { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal ClosingDebit { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal ClosingCredit { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class FinancialReportLine
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        [MaxLength(30)] public string ReportCode { get; set; } = "Balance";
        [MaxLength(30)] public string LineCode { get; set; } = string.Empty;
        [MaxLength(30)] public string SectionCode { get; set; } = string.Empty;
        [MaxLength(300)] public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public int Sign { get; set; } = 1;
        public bool IsTotal { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class FinancialReportLineAccount
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid LineId { get; set; }
        [MaxLength(50)] public string AccountCode { get; set; } = string.Empty;
    }

    public class TaxJournalRecord
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        [MaxLength(20)] public string JournalType { get; set; } = "Purchase";
        public DateTime Date { get; set; }
        [MaxLength(50)] public string DocumentNumber { get; set; } = string.Empty;
        [MaxLength(100)] public string DocumentType { get; set; } = string.Empty;
        [MaxLength(300)] public string Organization { get; set; } = string.Empty;
        [MaxLength(50)] public string TaxType { get; set; } = string.Empty;
        [Column(TypeName = "numeric(18,2)")] public decimal AmountWithoutTax { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal VatAmount { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal SalesTaxAmount { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal TaxAmount { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal TotalAmount { get; set; }
        public Guid? SourceRecordId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class LocalizationEntry
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        [MaxLength(40)] public string Culture { get; set; } = "ru-RU";
        [MaxLength(200)] public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        [MaxLength(50)] public string Category { get; set; } = "System";
        public bool IsActive { get; set; } = true;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
