using System;

namespace BIS.ERP.Models
{
    public class PostingViewModel
    {
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string DebitAccount { get; set; } = string.Empty;
        public string CreditAccount { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal AmountCurrency { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public string Organization { get; set; } = string.Empty;
        public string Counterparty { get; set; } = string.Empty;
        public string Employee { get; set; } = string.Empty;
        public string Site { get; set; } = string.Empty;
        public string ResponsiblePerson { get; set; } = string.Empty;
        public Guid? DocumentId { get; set; }
    }
}