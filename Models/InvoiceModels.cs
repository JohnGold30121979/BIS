using System;
using System.Collections.Generic;

namespace BIS.ERP.Models
{
    public class InvoiceListRow
    {
        public Guid Id { get; set; }
        public string DocNumber { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public string OrganizationName { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string Basis { get; set; } = string.Empty;
        public bool IsPosted { get; set; }
        public string IsPostedDisplay => IsPosted ? "Да" : "Нет";
    }

    public class InvoiceLineRow
    {
        public Guid Id { get; set; }
        public int LineNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal AmountWithoutTax { get; set; }
        public decimal VatRate { get; set; }
        public decimal VatAmount { get; set; }
        public decimal SalesTaxRate { get; set; }
        public decimal SalesTaxAmount { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class InvoiceDocument
    {
        public Guid Id { get; set; }
        public string DocNumber { get; set; } = string.Empty;
        public DateTime DocDate { get; set; } = DateTime.Today;
        public string EsfNumber { get; set; } = string.Empty;
        public Guid? OrganizationId { get; set; }
        public string OrganizationName { get; set; } = string.Empty;
        public string CounterpartyAccountCode { get; set; } = string.Empty;
        public string PaymentKind { get; set; } = string.Empty;
        public string DeliveryKind { get; set; } = string.Empty;
        public string SupplyKind { get; set; } = string.Empty;
        public string Basis { get; set; } = string.Empty;
        public decimal AmountWithoutTax { get; set; }
        public decimal VatTotal { get; set; }
        public decimal SalesTaxTotal { get; set; }
        public decimal TotalAmount { get; set; }
        public bool IsPosted { get; set; }
        public List<InvoiceLineRow> Lines { get; set; } = new();
    }

    public static class InvoiceDocumentTypes
    {
        public const string SalesIssue = "Выписка счет-фактур";
        public const string PurchaseRegistration = "Регистрация счет-фактур";

        public static bool IsSales(string documentName) =>
            documentName.Equals(SalesIssue, StringComparison.OrdinalIgnoreCase);

        public static bool IsPurchase(string documentName) =>
            documentName.Equals(PurchaseRegistration, StringComparison.OrdinalIgnoreCase);
    }
}
