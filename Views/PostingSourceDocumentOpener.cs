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
        /// <returns>
        /// <c>null</c> — исходный документ не найден/не удалось определить (вызывающий код
        /// открывает детали самой проводки); иначе — результат закрытия диалога исходного
        /// документа (<c>true</c> — документ был изменен и сохранен).
        /// </returns>
        public static async Task<bool?> TryOpenAsync(
            PostingViewModel posting,
            MetadataService? metadataService,
            Window? owner,
            bool isReadOnly)
        {
            if (string.IsNullOrWhiteSpace(posting.DocumentType) ||
                string.IsNullOrWhiteSpace(posting.DocumentNumber))
            {
                return null;
            }

            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            metadataService ??= new MetadataService(context);

            // Документы закрытого учетного периода всегда открываются только для просмотра.
            var effectiveReadOnly = isReadOnly;
            if (!effectiveReadOnly && !await CanModifyDocumentDateAsync(context, posting.Date))
            {
                effectiveReadOnly = true;
                MessageBox.Show(
                    $"Период, содержащий дату {posting.Date:dd.MM.yyyy}, закрыт. Документ открывается в режиме просмотра.",
                    "Закрытый период",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            var documents = await metadataService.GetDocumentsAsync();
            var documentMetadata = ResolveSourceDocumentMetadata(documents, posting.DocumentType);
            if (documentMetadata == null)
                return null;

            if (InvoiceDocumentTypes.IsSales(posting.DocumentType) ||
                InvoiceDocumentTypes.IsPurchase(posting.DocumentType))
            {
                return await TryOpenInvoiceAsync(posting, documentMetadata, metadataService, owner, effectiveReadOnly);
            }

            var recordId = await FindDynamicDocumentRecordIdAsync(
                documentMetadata,
                metadataService,
                posting.DocumentNumber,
                posting.Date,
                posting.DocumentType);
            if (!recordId.HasValue)
                return null;

            // Кассовые ордера (ПКО/РКО) открываются специализированным диалогом
            // CashOrderDialog: из проводки — просмотр, по «Корректировке» — редактирование.
            if (documentMetadata.Name.Equals("Расходный/Приходный КО", StringComparison.OrdinalIgnoreCase) ||
                posting.DocumentType.Equals("Приходный кассовый ордер", StringComparison.OrdinalIgnoreCase) ||
                posting.DocumentType.Equals("Расходный кассовый ордер", StringComparison.OrdinalIgnoreCase))
            {
                var cashDialog = new CashOrderDialog(
                    documentMetadata,
                    metadataService,
                    recordId.Value,
                    isReadOnly: effectiveReadOnly);

                return await MdiDialogService.ShowInWorkspaceForResultAsync(
                    owner,
                    cashDialog,
                    $"{(effectiveReadOnly ? "Просмотр" : "Редактирование")}: {posting.DocumentType} №{posting.DocumentNumber}");
            }

            var dialog = new DynamicDocumentItemDialog(
                documentMetadata,
                metadataService,
                recordId.Value,
                effectiveReadOnly);
            return await MdiDialogService.ShowInWorkspaceForResultAsync(
                owner,
                dialog,
                $"{(effectiveReadOnly ? "Просмотр" : "Редактирование")}: {documentMetadata.Name}");
        }

        /// <summary>
        /// Проверяет, можно ли изменять документ с указанной датой:
        /// даты, попадающие в итогово закрытый учетный период, блокируются.
        /// </summary>
        private static async Task<bool> CanModifyDocumentDateAsync(
            BIS.ERP.Data.AppDbContext context,
            DateTime date)
        {
            try
            {
                var periodService = new AccountingPeriodService(context);
                await periodService.EnsureDateCanBeModifiedAsync(date);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static MetadataObject? ResolveSourceDocumentMetadata(
            IReadOnlyCollection<MetadataObject> documents,
            string postingDocumentType)
        {
            var normalizedDocumentType = NormalizeDocumentTypeForLookup(postingDocumentType);
            return documents.FirstOrDefault(document =>
                document.Name.Equals(normalizedDocumentType, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeDocumentTypeForLookup(string documentType)
        {
            if (documentType.Equals("Исходящее платежное поручение", StringComparison.OrdinalIgnoreCase) ||
                documentType.Equals("Входящее платежное поручение", StringComparison.OrdinalIgnoreCase))
            {
                return "Платежное поручение";
            }

            if (documentType.Equals("Приходный кассовый ордер", StringComparison.OrdinalIgnoreCase) ||
                documentType.Equals("Расходный кассовый ордер", StringComparison.OrdinalIgnoreCase))
            {
                return "Расходный/Приходный КО";
            }

            return documentType;
        }

        private static async Task<bool?> TryOpenInvoiceAsync(
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
                return null;
            }

            var dialog = new InvoiceEditDialog(
                invoiceMetadata,
                metadataService,
                invoiceService,
                invoiceId.Value,
                isReadOnly);
            return await MdiDialogService.ShowInWorkspaceForResultAsync(
                owner,
                dialog,
                $"{(isReadOnly ? "Просмотр" : "Редактирование")}: {invoiceMetadata.Name}");
        }

        private static async Task<Guid?> FindDynamicDocumentRecordIdAsync(
            MetadataObject documentMetadata,
            MetadataService metadataService,
            string documentNumber,
            DateTime? documentDate,
            string postingDocumentType)
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

            var typeMatched = matches
                .Where(row => DocumentTypeMatches(
                    postingDocumentType,
                    documentMetadata.Name,
                    GetRowString(row, "Тип КО", "order_kind", "cash_order_kind", "Тип", "document_type", "DocumentType")))
                .ToList();

            if (typeMatched.Count > 0)
                matches = typeMatched;

            var dateMatched = documentDate.HasValue
                ? matches.FirstOrDefault(row =>
                    GetRowDate(row, "Дата", "doc_date", "date")?.Date == documentDate.Value.Date)
                : null;

            var selected = dateMatched ?? matches.First();
            return Guid.TryParse(GetRowString(selected, "Id"), out var id) ? id : null;
        }

        private static bool DocumentTypeMatches(
            string postingDocumentType,
            string metadataDocumentName,
            string recordDocumentType)
        {
            if (metadataDocumentName.Equals("Расходный/Приходный КО", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(recordDocumentType))
                    return true;

                var expectsReceipt = postingDocumentType.Equals("Приходный кассовый ордер", StringComparison.OrdinalIgnoreCase);
                var expectsPayment = postingDocumentType.Equals("Расходный кассовый ордер", StringComparison.OrdinalIgnoreCase);
                if (expectsReceipt)
                {
                    return recordDocumentType.Equals("Receipt", StringComparison.OrdinalIgnoreCase) ||
                           recordDocumentType.Contains("приход", StringComparison.OrdinalIgnoreCase);
                }

                if (expectsPayment)
                {
                    return recordDocumentType.Equals("Payment", StringComparison.OrdinalIgnoreCase) ||
                           recordDocumentType.Contains("расход", StringComparison.OrdinalIgnoreCase);
                }
            }

            if (string.IsNullOrWhiteSpace(recordDocumentType))
                return true;

            return recordDocumentType.Equals(postingDocumentType, StringComparison.OrdinalIgnoreCase) ||
                   recordDocumentType.Equals(metadataDocumentName, StringComparison.OrdinalIgnoreCase);
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

