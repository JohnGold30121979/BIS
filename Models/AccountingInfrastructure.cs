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

    public class FixedAssetPeriodBalance
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PeriodId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public Guid AssetId { get; set; }
        [MaxLength(50)] public string InventoryNumber { get; set; } = string.Empty;
        [MaxLength(300)] public string AssetName { get; set; } = string.Empty;
        public Guid? OrganizationId { get; set; }
        [MaxLength(300)] public string OrganizationName { get; set; } = string.Empty;
        public Guid? ResponsiblePersonId { get; set; }
        [MaxLength(300)] public string ResponsiblePersonName { get; set; } = string.Empty;
        public Guid? SiteId { get; set; }
        [MaxLength(300)] public string SiteName { get; set; } = string.Empty;
        [MaxLength(100)] public string AssetAccount { get; set; } = string.Empty;
        [MaxLength(100)] public string DepreciationAccount { get; set; } = string.Empty;
        [MaxLength(100)] public string ExpenseAccount { get; set; } = string.Empty;
        public DateTime? AcquisitionDate { get; set; }
        public DateTime? CommissioningDate { get; set; }
        public DateTime? DepreciationStartDate { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal InitialCost { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal SalvageValue { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal AccumulatedDepreciation { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal CarryingAmount { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal MonthlyDepreciation { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal OpeningCost { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal OpeningDepreciation { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal OpeningCarryingAmount { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal AcquisitionCost { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal DisposalCost { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal TransferInCost { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal TransferOutCost { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal RevaluationCost { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal AutomaticDepreciation { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal ManualDepreciation { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal DepreciationAdjustment { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal DepreciationWriteOff { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal DisposalDepreciation { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal TransferInDepreciation { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal TransferOutDepreciation { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal ClosingCost { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal ClosingDepreciation { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal ClosingCarryingAmount { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal OpeningMileage { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal PeriodMileage { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal ClosingMileage { get; set; }
        public bool IsActive { get; set; }
        [MaxLength(100)] public string LifecycleStatus { get; set; } = string.Empty;
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
        [MaxLength(500)] public string Formula { get; set; } = string.Empty;
        [Column(TypeName = "numeric(18,2)")] public decimal FixedAmount { get; set; }
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
