using System;
using System.Collections.Generic;

namespace BIS.ERP.Models
{
    public class OrganizationBalanceCalculationResult
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public List<OrganizationBalanceRow> Rows { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class OrganizationBalanceRow
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public Guid? OrganizationId { get; set; }
        public string OrganizationName { get; set; } = string.Empty;
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string CounterAccountCode { get; set; } = string.Empty;
        public string CounterAccountName { get; set; } = string.Empty;
        public string AccountPairName { get; set; } = string.Empty;
        public string ModuleCode { get; set; } = string.Empty;
        public bool UsesCurrency { get; set; }
        public bool IsOrganizationTotal { get; set; }
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
        public decimal TurnoverDebit { get; set; }
        public decimal TurnoverCredit { get; set; }
        public decimal ClosingDebit { get; set; }
        public decimal ClosingCredit { get; set; }
        public decimal OpeningDebitCurrency { get; set; }
        public decimal OpeningCreditCurrency { get; set; }
        public decimal TurnoverDebitCurrency { get; set; }
        public decimal TurnoverCreditCurrency { get; set; }
        public decimal ClosingDebitCurrency { get; set; }
        public decimal ClosingCreditCurrency { get; set; }
        public decimal Balance => ClosingDebit - ClosingCredit;
        public decimal CurrencyBalance => ClosingDebitCurrency - ClosingCreditCurrency;
    }
}
