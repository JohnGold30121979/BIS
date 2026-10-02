using System;

namespace BIS.ERP.Models
{
    /// <summary>
    /// Строка затрат авансового платежа. Хранится в отдельной таблице
    /// doc_advance_payment_lines — классическая нормализованная модель,
    /// как у счет-фактур (doc_sales_invoice_lines / doc_purchase_invoice_lines).
    /// </summary>
    public class AdvancePaymentLine
    {
        public Guid Id { get; set; }
        public int LineNumber { get; set; }
        public DateTime? LineDate { get; set; }

        /// <summary>Вид авансового расчета (пара счетов).</summary>
        public Guid PairId { get; set; }
        public string PairCode { get; set; } = string.Empty;
        public string PairName { get; set; } = string.Empty;
        public string DebitAccount { get; set; } = string.Empty;
        public string CreditAccount { get; set; } = string.Empty;
        public string ExpenseAccount { get; set; } = string.Empty;
        public string ExpenseAccountName { get; set; } = string.Empty;
        public Guid CurrencyId { get; set; }
        public string CurrencyName { get; set; } = string.Empty;
        public decimal AmountCurrency { get; set; }
        public decimal ExchangeRate { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}