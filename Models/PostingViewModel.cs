using System;

namespace BIS.ERP.Models
{
    public class PostingViewModel
    {
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string ModuleCode { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string DebitAccount { get; set; } = string.Empty;
        public string DebitAccountName { get; set; } = string.Empty;
        public string CreditAccount { get; set; } = string.Empty;
        public string CreditAccountName { get; set; } = string.Empty;
        public string CorrespondentAccount { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal AmountCurrency { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public string Organization { get; set; } = string.Empty;
        public string Employee { get; set; } = string.Empty;
        public string Site { get; set; } = string.Empty;
        public string ResponsiblePerson { get; set; } = string.Empty;
        public Guid? DocumentId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public string DetailHint =>
            $"{Date:dd.MM.yyyy} {FormatModuleHint()}{DocumentType} N {DocumentNumber}: Дт {DebitAccount} / Кт {CreditAccount}, {Amount:N2} сом";

        private string FormatModuleHint() =>
            string.IsNullOrWhiteSpace(ModuleName) ? string.Empty : $"{ModuleName}: ";
    }
}
