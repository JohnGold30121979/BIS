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
        public Dictionary<int, decimal> MonthlyTurnoverDebit { get; set; } = new();
        public Dictionary<int, decimal> MonthlyTurnoverCredit { get; set; } = new();
        public decimal YearTurnoverDebit { get; set; }
        public decimal YearTurnoverCredit { get; set; }
    }

    /// <summary>
    /// Баланс предприятия
    /// </summary>
    public class EnterpriseBalance
    {
        public List<BalanceItem> Assets { get; set; } = new();
        public List<BalanceItem> Liabilities { get; set; } = new();
        public decimal TotalAssets { get; set; }
        public decimal TotalLiabilities { get; set; }
    }

    public class BalanceItem
    {
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}