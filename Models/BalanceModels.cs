namespace BIS.ERP.Models
{
    /// <summary>
    /// Оборотный баланс
    /// </summary>
    public class TurnoverBalance
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
        public decimal TurnoverDebit { get; set; }
        public decimal TurnoverCredit { get; set; }
        public decimal ClosingDebit { get; set; }
        public decimal ClosingCredit { get; set; }
    }

    /// <summary>
    /// Главная книга
    /// </summary>
    public class GeneralLedger
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
        public Dictionary<int, decimal> MonthlyTurnoverDebit { get; set; } = new();
        public Dictionary<int, decimal> MonthlyTurnoverCredit { get; set; } = new();
        public decimal YearTurnoverDebit { get; set; }
        public decimal YearTurnoverCredit { get; set; }
        public decimal ClosingDebit { get; set; }
        public decimal ClosingCredit { get; set; }
    }

    /// <summary>
    /// Баланс предприятия
    /// </summary>
    public class EnterpriseBalance
    {
        public List<BalanceItem> Assets { get; set; } = new();
        public List<BalanceItem> Liabilities { get; set; } = new();
        public List<BalanceItem> Equity { get; set; } = new();
        public decimal TotalAssets { get; set; }
        public decimal TotalLiabilities { get; set; }
        public decimal TotalEquity { get; set; }
        public decimal TotalLiabilitiesAndEquity => TotalLiabilities + TotalEquity;
        public decimal Difference => TotalAssets - TotalLiabilitiesAndEquity;
        public bool IsBalanced => Math.Abs(Difference) < 0.01m;
    }

    public class BalanceItem
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class FinancialResults
    {
        public List<BalanceItem> Income { get; set; } = new();
        public List<BalanceItem> Expenses { get; set; } = new();
        public decimal? TotalIncomeOverride { get; set; }
        public decimal? TotalExpensesOverride { get; set; }
        public decimal TotalIncome => TotalIncomeOverride ?? Income.Sum(item => item.Amount);
        public decimal TotalExpenses => TotalExpensesOverride ?? Expenses.Sum(item => item.Amount);
        public decimal ProfitOrLoss => TotalIncome - TotalExpenses;
    }

    public class PurchaseSaleJournalEntry
    {
        public string Section { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string Organization { get; set; } = string.Empty;
        public string ModuleCode { get; set; } = string.Empty;
        public string TaxBlankNumber { get; set; } = string.Empty;
        public string EsfNumber { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal AmountWithoutTax { get; set; }
        public decimal VatAmount { get; set; }
        public decimal SalesTaxAmount { get; set; }
        public decimal TaxAmount { get; set; }     
        public string TaxType { get; set; } = string.Empty;
        public string VatTaxCode { get; set; } = string.Empty;
        public string SalesTaxCode { get; set; } = string.Empty;
        public bool IsPosted { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    public class PeriodDocumentSummary
    {
        public string DocumentType { get; set; } = string.Empty;
        public int DocumentCount { get; set; }
        public int PostedCount { get; set; }
        public int UnpostedCount => DocumentCount - PostedCount;
        public decimal Amount { get; set; }
    }

    public class PeriodCollectionResult
    {
        public List<PeriodDocumentSummary> Documents { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public int PostingCount { get; set; }
        public decimal DebitTurnover { get; set; }
        public decimal CreditTurnover { get; set; }
        public decimal Difference => DebitTurnover - CreditTurnover;
        public bool IsBalanced => Math.Abs(Difference) < 0.01m;
    }
}
