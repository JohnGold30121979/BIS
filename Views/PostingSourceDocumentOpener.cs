using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;

namespace BIS.ERP.Views
{
    internal static class PostingSourceDocumentOpener
    {
        public static async Task<bool> TryOpenAsync(
            PostingViewModel posting,
            MetadataService? metadataService,
            Window? owner,
            bool isReadOnly)
        {
            if (string.IsNullOrWhiteSpace(posting.DocumentType) ||
                string.IsNullOrWhiteSpace(posting.DocumentNumber))
            {
                return false;
            }

            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            metadataService ??= new MetadataService(context);

            var documents = await metadataService.GetDocumentsAsync();
            var documentMetadata = documents.FirstOrDefault(document =>
                document.Name.Equals(posting.DocumentType, StringComparison.OrdinalIgnoreCase));
            if (documentMetadata == null)
                return false;

            if (InvoiceDocumentTypes.IsSales(posting.DocumentType) ||
                InvoiceDocumentTypes.IsPurchase(posting.DocumentType))
            {
                return await TryOpenInvoiceAsync(posting, documentMetadata, metadataService, owner, isReadOnly);
            }

            var recordId = await FindDynamicDocumentRecordIdAsync(
                documentMetadata,
                metadataService,
                posting.DocumentNumber,
                posting.Date);
            if (!recordId.HasValue)
                return false;

            var dialog = new DynamicDocumentItemDialog(
                documentMetadata,
                metadataService,
                recordId.Value,
                isReadOnly);
            dialog.Owner = owner;
            dialog.ShowDialog();
            return true;
        }

        private static async Task<bool> TryOpenInvoiceAsync(
            PostingViewModel posting,
            MetadataObject invoiceMetadata,
            MetadataService metadataService,
            Window? owner,
            bool isReadOnly)
        {
            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var invoiceService = new InvoiceService(context);
            invoiceService.Configure(invoiceMetadata);
            await invoiceService.EnsureSchemaAsync();

            var invoiceId = await invoiceService.FindInvoiceIdByPostingNumberAsync(
                posting.DocumentNumber,
                posting.Date);

            if (!invoiceId.HasValue)
            {
                MessageBox.Show(
                    $"Проводка есть, но исходная счет-фактура №{posting.DocumentNumber} не найдена. Будут открыты детали проводки.",
                    "Счет-фактура",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            var dialog = new InvoiceEditDialog(
                invoiceMetadata,
                metadataService,
                invoiceService,
                invoiceId.Value,
                isReadOnly);
            dialog.Owner = owner;
            dialog.ShowDialog();
            return true;
        }

        private static async Task<Guid?> FindDynamicDocumentRecordIdAsync(
            MetadataObject documentMetadata,
            MetadataService metadataService,
            string documentNumber,
            DateTime? documentDate)
        {
            var normalizedNumber = MetadataService.NormalizeLegacyDocumentNumber(documentNumber);
            if (string.IsNullOrWhiteSpace(normalizedNumber))
                return null;

            var rows = await metadataService.GetCatalogDataAsync(documentMetadata.Id);
            var matches = rows
                .Where(row => string.Equals(
                    MetadataService.NormalizeLegacyDocumentNumber(GetRowString(row, "Номер", "Номер документа", "doc_number", "number")),
                    normalizedNumber,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                return null;

            var dateMatched = documentDate.HasValue
                ? matches.FirstOrDefault(row =>
                    GetRowDate(row, "Дата", "doc_date", "date")?.Date == documentDate.Value.Date)
                : null;

            var selected = dateMatched ?? matches.First();
            return Guid.TryParse(GetRowString(selected, "Id"), out var id) ? id : null;
        }

        private static string GetRowString(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                var pair = row.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                var value = pair.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }

        private static DateTime? GetRowDate(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                var pair = row.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (pair.Value is DateTime date)
                    return date;
                if (DateTime.TryParse(pair.Value?.ToString(), out date))
                    return date;
            }

            return null;
        }
    }
}
