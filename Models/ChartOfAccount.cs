using System;
using System.ComponentModel.DataAnnotations;

namespace BIS.ERP.Models
{
    public class ChartOfAccount
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(20)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public int Level { get; set; } = 1;

        public string AccountType { get; set; } = "Active"; // Active, Passive, ActivePassive

        public Guid? ParentId { get; set; }

        public int Order { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        // Служебные признаки, считанные из Fox-источника.
        public int FoxAccountSign { get; set; }
        public int FoxVatFlag { get; set; }
        public int FoxFixedAssetFlag { get; set; }
        public bool UseOrganizations { get; set; }
        public bool UseEmployees { get; set; }
        public bool UseMaterials { get; set; }
        public bool UseCurrencies { get; set; }
        public bool UsePersonalAccounts { get; set; }
        public int TaxCode { get; set; }
        public int ReportBreakdownCode { get; set; }
        public int ReportFormCode { get; set; }
        public int AnalyticsGroupCode { get; set; }
        public int ClosingSubsystemCode { get; set; }
        public int PrintModeCode { get; set; }
        public int BalanceModeCode { get; set; }
        public bool UseSites { get; set; }
        public bool UseJournal { get; set; }
        public string BalanceLineCode { get; set; } = string.Empty;
    }
}
