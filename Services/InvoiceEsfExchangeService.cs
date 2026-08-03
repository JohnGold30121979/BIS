using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace BIS.ERP.Services
{
    public sealed class InvoiceEsfExchangeService
    {
        private static readonly TimeSpan KyrgyzstanOffset = TimeSpan.FromHours(6);

        private readonly AppDbContext _context;
        private readonly InvoiceService _invoiceService;
        private readonly MetadataService _metadataService;
        private readonly ITaxEsfApiClient _apiClient;

        public InvoiceEsfExchangeService(
            AppDbContext context,
            InvoiceService invoiceService,
            ITaxEsfApiClient? apiClient = null)
        {
            _context = context;
            _invoiceService = invoiceService;
            _metadataService = new MetadataService(context);
            _apiClient = apiClient ?? new DisabledTaxEsfApiClient();
        }

        public bool CanUseApiIntegration => _apiClient.IsConfigured;

        public async Task<InvoiceEsfExportResult> ExportSelectedInvoicesAsync(
            IReadOnlyCollection<Guid> invoiceIds,
            string outputPath)
        {
            if (invoiceIds.Count == 0)
                throw new InvalidOperationException("Не выбраны счет-фактуры для выгрузки.");

            var invoices = await LoadInvoicesAsync(invoiceIds);
            return await ExportInvoicesAsync(invoices, outputPath);
        }

        public async Task<InvoiceEsfExportResult> ExportPeriodAsync(
            DateTime startDate,
            DateTime endDate,
            bool onlyNotExported,
            string outputPath)
        {
            EnsureSalesMode();

            var invoiceIds = await LoadInvoiceIdsForPeriodAsync(startDate.Date, endDate.Date, onlyNotExported);
            if (invoiceIds.Count == 0)
            {
                throw new InvalidOperationException(
                    onlyNotExported
                        ? "За выбранный период нет невыгруженных счет-фактур."
                        : "За выбранный период нет счет-фактур для выгрузки.");
            }

            var invoices = await LoadInvoicesAsync(invoiceIds);
            return await ExportInvoicesAsync(invoices, outputPath);
        }

        public async Task<InvoiceEsfImportResult> ImportResponseAsync(string inputPath)
        {
            EnsureSalesMode();

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Файл ответа налоговой не найден.", inputPath);

            var document = XDocument.Load(inputPath, LoadOptions.PreserveWhitespace);
            var receipts = GetReceiptElements(document).ToList();
            if (receipts.Count == 0)
                throw new InvalidOperationException("В файле не найдено ни одной записи receipt.");

            EnsureTaxResponseReceipts(receipts);

            var updated = 0;
            var skippedDuplicates = 0;
            var unmatched = new List<string>();

            foreach (var receipt in receipts)
            {
                var imported = ImportedReceipt.FromXml(receipt);
                if (string.IsNullOrWhiteSpace(imported.ExchangeCode))
                {
                    unmatched.Add(BuildUnmatchedDescription(imported));
                    continue;
                }

                // Проверка на дубликат: если exchangeCode уже существует в базе у другого документа, пропускаем
                var existingInvoiceId = await _invoiceService.FindInvoiceIdByExchangeCodeAsync(imported.ExchangeCode);
                var resolvedInvoiceId = await ResolveInvoiceIdAsync(imported);

                // Если exchangeCode уже существует и принадлежит другому документу - это дубликат
                if (existingInvoiceId.HasValue && resolvedInvoiceId.HasValue && existingInvoiceId.Value != resolvedInvoiceId.Value)
                {
                    skippedDuplicates++;
                    continue;
                }

                var invoiceId = resolvedInvoiceId;
                if (!invoiceId.HasValue)
                {
                    unmatched.Add(BuildUnmatchedDescription(imported));
                    continue;
                }

                var invoice = await _invoiceService.GetInvoiceAsync(invoiceId.Value);
                if (invoice == null)
                {
                    unmatched.Add(BuildUnmatchedDescription(imported));
                    continue;
                }
                // Для ответного файла обновляем только exchangeCode (как в FoxPro)
                if (string.Equals(imported.ExchangeCode, invoice.ExchangeCode, StringComparison.OrdinalIgnoreCase))
                {
                    skippedDuplicates++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(imported.ExchangeCode))
                {
                    await _invoiceService.UpdateEsfExchangeCodeAsync(invoice.Id, imported.ExchangeCode);
                    updated++;
                }
            }

            return new InvoiceEsfImportResult(receipts.Count, updated, unmatched, skippedDuplicates);
        }

        public async Task<TaxEsfApiSubmitResult> SubmitSelectedInvoicesViaApiAsync(
            IReadOnlyCollection<Guid> invoiceIds,
            CancellationToken cancellationToken = default)
        {
            if (!_apiClient.IsConfigured)
                throw new InvalidOperationException("API интеграция с налоговой пока не настроена.");

            var invoices = await LoadInvoicesAsync(invoiceIds);
            var preparedExport = await PrepareExportAsync(invoices);
            return await _apiClient.SubmitAsync(preparedExport.Payload, cancellationToken);
        }

        private async Task<InvoiceEsfExportResult> ExportInvoicesAsync(
            IReadOnlyCollection<InvoiceDocument> invoices,
            string outputPath)
        {
            EnsureSalesMode();

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new InvalidOperationException("Не указан путь для сохранения XML.");

            var preparedExport = await PrepareExportAsync(invoices);
            await File.WriteAllTextAsync(outputPath, preparedExport.Payload, new UTF8Encoding(false));

            foreach (var exportedInvoice in preparedExport.Invoices)
            {
                var invoice = invoices.First(item => item.Id == exportedInvoice.InvoiceId);
                await _invoiceService.UpdateEsfExchangeInfoAsync(
                    invoice.Id,
                    invoice.EsfNumber,
                    exportedInvoice.ExchangeCode,
                    exportedInvoice.Status,
                    preparedExport.ExportedAt,
                    invoice.TaxStatusDate);
            }

            return new InvoiceEsfExportResult(invoices.Count, outputPath);
        }

        private async Task<PreparedExportBatch> PrepareExportAsync(
            IReadOnlyCollection<InvoiceDocument> invoices)
        {
            if (invoices.Count == 0)
                throw new InvalidOperationException("Нет счет-фактур для выгрузки.");

            var (primaryOrganization, organizationsById) = await LoadOrganizationsAsync();
            var referenceMaps = await LoadEsfReferenceMapsAsync();
            var tagNames = await LoadEsfXmlTagNamesAsync();
            if (primaryOrganization == null)
                throw new InvalidOperationException(
                    "Не найден справочник 'Организации' с реквизитами предприятия. Заполните первичную организацию.");

            ValidateInvoices(invoices, primaryOrganization, organizationsById);

            var usedReceiptCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var receiptElements = new List<XElement>();
            var preparedInvoices = new List<PreparedExportInvoice>();
            var exportedAt = DateTime.Now;

            foreach (var invoice in invoices.OrderBy(item => item.DocDate).ThenBy(item => item.DocNumber, StringComparer.OrdinalIgnoreCase))
            {
                var counterparty = invoice.OrganizationId.HasValue && organizationsById.TryGetValue(invoice.OrganizationId.Value, out var selected)
                    ? selected
                    : throw new InvalidOperationException(
                        $"Для счет-фактуры {invoice.DocNumber} не найдена организация-контрагент.");

                var receiptCode = BuildOwnedCrmReceiptCode(invoice);
                if (!usedReceiptCodes.Add(receiptCode))
                {
                    throw new InvalidOperationException(
                        $"Код выгрузки '{receiptCode}' повторяется в выбранном наборе счет-фактур. " +
                        "Уточните номера документов или номера бланков.");
                }

                var exchangeCode = string.IsNullOrWhiteSpace(invoice.ExchangeCode)
                    ? Guid.NewGuid().ToString().ToUpperInvariant()
                    : invoice.ExchangeCode.Trim();
                var status = string.IsNullOrWhiteSpace(invoice.TaxStatus) ? "Новый" : invoice.TaxStatus.Trim();

                receiptElements.Add(BuildReceiptElement(
                    invoice,
                    primaryOrganization,
                    counterparty,
                    exchangeCode,
                    status,
                    receiptCode,
                    exportedAt,
                    referenceMaps,
                    tagNames));
                preparedInvoices.Add(new PreparedExportInvoice(invoice.Id, exchangeCode, status));
            }

            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            var xml = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Tag(tagNames, "VFPDataSet"),
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(xsi + "noNamespaceSchemaLocation", "result.xsd"),
                    new XElement(Tag(tagNames, "receipts"), receiptElements)));

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                OmitXmlDeclaration = false
            };

            using var stringWriter = new Utf8StringWriter();
            using (var writer = XmlWriter.Create(stringWriter, settings))
            {
                xml.Save(writer);
            }

            return new PreparedExportBatch(stringWriter.ToString(), preparedInvoices, exportedAt);
        }

        private XElement BuildReceiptElement(
            InvoiceDocument invoice,
            OrganizationEsfInfo issuer,
            OrganizationEsfInfo counterparty,
            string exchangeCode,
            string documentStatus,
            string receiptCode,
            DateTime exportedAt,
            EsfReferenceMaps referenceMaps,
            EsfXmlTagNames tagNames)
        {
            var createdDate = exportedAt.Date;
            var invoiceDate = new DateTimeOffset(
                DateTime.SpecifyKind(invoice.DocDate.Date, DateTimeKind.Unspecified),
                KyrgyzstanOffset);
            var note = string.IsNullOrWhiteSpace(invoice.Basis)
                ? invoice.Lines.FirstOrDefault()?.Name ?? string.Empty
                : invoice.Basis.Trim();

            return new XElement(Tag(tagNames, "receipt"),
                Element(tagNames, "exchangeCode", exchangeCode),
                Element(tagNames, "receiptTypeCode", ResolveReceiptTypeCode()),
                Element(tagNames, "createdDate", createdDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Element(tagNames, "ownedCrmReceiptCode", receiptCode),
                Element(tagNames, "correctedReceiptCode", string.Empty),
                Element(tagNames, "correctionReasonCode", string.Empty),
                Element(tagNames, "bankAccount", issuer.BankAccount),
                Element(tagNames, "contractorPin", counterparty.Inn),
                Element(tagNames, "contractorBankAccount", counterparty.BankAccount),
                Element(tagNames, "deliveryContractNumber", string.Empty),
                Element(tagNames, "deliveryContractDate", string.Empty),
                Element(tagNames, "goodsDeliveryTypeCode", "0"),
                Element(tagNames, "paymentTypeCode", ResolvePaymentTypeCode(invoice, referenceMaps.PaymentKinds)),
                Element(tagNames, "invoiceDeliveryTypeCode", ResolveInvoiceDeliveryTypeCode(invoice, referenceMaps.DeliveryKinds)),
                Element(tagNames, "vatDeliveryTypeCode", ResolveVatDeliveryTypeCode(invoice, referenceMaps.SupplyKinds)),
                Element(tagNames, "currencyCode", "417"),
                Element(tagNames, "exchangeRate", "1"),
                Element(tagNames, "contractorCitizenshipCode", "417"),
                Element(tagNames, "isPriceWithoutTaxes", "true"),
                Element(tagNames, "note", note),
                Element(tagNames, "vatCode", ResolveVatCode(invoice, referenceMaps.Taxes)),
                Element(tagNames, "isResident", "true"),
                Element(tagNames, "foreignName", string.IsNullOrWhiteSpace(counterparty.FullName) ? counterparty.Name : counterparty.FullName),
                Element(tagNames, "sellerBranchPin", string.Empty),
                Element(tagNames, "isIndustry", "false"),
                Element(tagNames, "openingBalances", DecimalText(0m)),
                Element(tagNames, "assessedContributionsAmount", DecimalText(0m)),
                Element(tagNames, "paidAmount", DecimalText(0m)),
                Element(tagNames, "penaltiesAmount", DecimalText(0m)),
                Element(tagNames, "finesAmount", DecimalText(0m)),
                Element(tagNames, "closingBalances", DecimalText(0m)),
                Element(tagNames, "amountToBePaid", DecimalText(0m)),
                Element(tagNames, "personalAccountNumber", "0"),
                Element(tagNames, "markGoods", "false"),
                Element(tagNames, "invoiceDate", invoiceDate.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)),
                Element(tagNames, "invoiceNumber", invoice.EsfNumber),
                Element(tagNames, "contractorName", string.IsNullOrWhiteSpace(counterparty.FullName) ? counterparty.Name : counterparty.FullName),
                Element(tagNames, "contractorBranchName", string.Empty),
                Element(tagNames, "currencyName", "Сом"),
                Element(tagNames, "contractorCitizenshipName", "417"),
                Element(tagNames, "correctedReceiptCreationDate", string.Empty),
                Element(tagNames, "correctionReasonName", string.Empty),
                Element(tagNames, "documentStatusName", documentStatus),
                Element(tagNames, "correctionSeries", string.Empty),
                Element(tagNames, "type", "10"),
                Element(tagNames, "costWithoutTaxes", DecimalText(invoice.AmountWithoutTax)),
                Element(tagNames, "totalCost", DecimalText(invoice.TotalAmount)),
                new XElement(Tag(tagNames, "goods"),
                    invoice.Lines.OrderBy(item => item.LineNumber)
                        .Select(line => BuildGoodElement(line, referenceMaps.Taxes, tagNames))));
        }

        private static XElement BuildGoodElement(
            InvoiceLineRow line,
            IReadOnlyDictionary<string, EsfCatalogEntry> taxesByCode,
            EsfXmlTagNames tagNames)
        {
            var quantity = line.Quantity <= 0 ? 1m : line.Quantity;
            var price = quantity == 0 ? line.AmountWithoutTax : Math.Round(line.AmountWithoutTax / quantity, 5);

            return new XElement(Tag(tagNames, "good"),
                Element(tagNames, "vatAmount", DecimalText(line.VatAmount)),
                Element(tagNames, "stCode", ResolveSalesTaxCode(line, taxesByCode)),
                Element(tagNames, "stAmount", DecimalText(line.SalesTaxAmount)),
                Element(tagNames, "goodsName", line.Name),
                Element(tagNames, "baseCount", DecimalText(quantity, 5)),
                Element(tagNames, "price", DecimalText(price, 5)));
        }

        private async Task<IReadOnlyCollection<InvoiceDocument>> LoadInvoicesAsync(IReadOnlyCollection<Guid> invoiceIds)
        {
            EnsureSalesMode();

            var invoices = new List<InvoiceDocument>();
            foreach (var invoiceId in invoiceIds.Distinct())
            {
                var invoice = await _invoiceService.GetInvoiceAsync(invoiceId);
                if (invoice == null)
                    continue;
                if (!invoice.IsPosted)
                    throw new InvalidOperationException(
                        $"Счет-фактура {invoice.DocNumber} не проведена. Выгружать можно только проведенные документы.");
                if (invoice.Lines.Count == 0)
                    throw new InvalidOperationException(
                        $"Счет-фактура {invoice.DocNumber} не содержит строк.");
                invoices.Add(invoice);
            }

            if (invoices.Count == 0)
                throw new InvalidOperationException("Не удалось загрузить выбранные счет-фактуры.");

            return invoices;
        }

        private async Task<List<Guid>> LoadInvoiceIdsForPeriodAsync(
            DateTime startDate,
            DateTime endDate,
            bool onlyNotExported)
        {
            var sql = new StringBuilder($@"
                SELECT ""Id""
                FROM ""{_invoiceService.HeaderTableName}""
                WHERE DATE(""doc_date"") BETWEEN DATE(@startDate) AND DATE(@endDate)
                  AND COALESCE(""is_posted"", false) = true");

            if (onlyNotExported)
                sql.Append(@" AND ""exported_at"" IS NULL");

            sql.Append(@" ORDER BY ""doc_date"", ""doc_number""");

            var result = new List<Guid>();
            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql.ToString();
            command.Parameters.Add(new NpgsqlParameter("@startDate", startDate.Date));
            command.Parameters.Add(new NpgsqlParameter("@endDate", endDate.Date));
            await _context.Database.OpenConnectionAsync();
            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    result.Add(reader.GetGuid(0));
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return result;
        }

        private async Task<(OrganizationEsfInfo? Primary, Dictionary<Guid, OrganizationEsfInfo> ById)> LoadOrganizationsAsync()
        {
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Организации");
            if (catalog == null)
                return (null, new Dictionary<Guid, OrganizationEsfInfo>());

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            var organizations = new Dictionary<Guid, OrganizationEsfInfo>();
            OrganizationEsfInfo? primary = null;

            foreach (var row in rows)
            {
                if (!TryGetRowId(row, out var rowId))
                    continue;

                var info = new OrganizationEsfInfo(
                    rowId,
                    GetString(row, "name", "Наименование"),
                    GetString(row, "full_name", "Полное наименование", "name", "Наименование"),
                    GetString(row, "inn", "ИНН"),
                    GetString(row, "okpo", "ОКПО"),
                    GetString(row, "bank_account", "Расчетный счет"),
                    GetString(row, "legal_address", "Юридический адрес", "actual_address", "Фактический адрес"),
                    GetString(row, "bank_name", "Банк"),
                    GetString(row, "bic", "БИК"),
                    GetString(row, "director", "Руководитель"),
                    GetString(row, "chief_accountant", "Главный бухгалтер"),
                    GetBoolean(row, "is_primary", "Первичная организация"));

                organizations[rowId] = info;
                if (primary == null && info.IsPrimary)
                    primary = info;
            }

            primary ??= organizations.Values.FirstOrDefault();
            return (primary, organizations);
        }

        private async Task<EsfReferenceMaps> LoadEsfReferenceMapsAsync()
        {
            return new EsfReferenceMaps(
                await LoadCatalogEntriesAsync("Виды оплаты"),
                await LoadCatalogEntriesAsync("Виды поставки"),
                await LoadCatalogEntriesAsync("Типы поставки"),
                await LoadCatalogEntriesAsync("Налоги"));
        }

        private async Task<EsfXmlTagNames> LoadEsfXmlTagNamesAsync()
        {
            var result = DefaultEsfXmlTagNames();
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Catalog" &&
                    (item.TableName == "catalog_esf_xml_tags" || item.Name == "Настройки XML ЭСФ"));
            if (catalog == null)
                return new EsfXmlTagNames(result);

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            foreach (var row in rows)
            {
                if (!GetBoolean(row, "is_active", "Активен"))
                    continue;

                var key = GetString(row, "code", "Ключ");
                var tagName = GetString(row, "tag_name", "Имя XML-тега");
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(tagName))
                    continue;

                result[key.Trim()] = tagName.Trim();
            }

            return new EsfXmlTagNames(result);
        }

        private async Task<Dictionary<string, EsfCatalogEntry>> LoadCatalogEntriesAsync(string catalogName)
        {
            var result = new Dictionary<string, EsfCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == catalogName);
            if (catalog == null)
                return result;

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            foreach (var row in rows)
            {
                var code = GetString(row, "code", "Код");
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                result[code.Trim()] = new EsfCatalogEntry(
                    code.Trim(),
                    GetString(row, "esf_code", "Код ЭСФ"),
                    GetString(row, "esf_vat_code", "Код ЭСФ НДС"),
                    GetString(row, "esf_sales_tax_code", "Код ЭСФ НСП"),
                    GetString(row, "name", "Наименование"));
            }

            return result;
        }

        private async Task<Guid?> ResolveInvoiceIdAsync(ImportedReceipt imported)
        {
            if (!string.IsNullOrWhiteSpace(imported.ExchangeCode))
            {
                var byExchange = await _invoiceService.FindInvoiceIdByExchangeCodeAsync(imported.ExchangeCode);
                if (byExchange.HasValue)
                    return byExchange;
            }

            var matchDate = imported.InvoiceDate?.Date ?? imported.CreatedDate?.Date;

            if (!string.IsNullOrWhiteSpace(imported.OwnedCrmReceiptCode))
            {
                var byTaxBlank = await _invoiceService.FindInvoiceIdByTaxBlankNumberAsync(imported.OwnedCrmReceiptCode, matchDate);
                if (byTaxBlank.HasValue)
                    return byTaxBlank;

                var byDocument = await _invoiceService.FindInvoiceIdAsync(imported.OwnedCrmReceiptCode, matchDate);
                if (byDocument.HasValue)
                    return byDocument;

                byTaxBlank = await _invoiceService.FindInvoiceIdByTaxBlankNumberAsync(imported.OwnedCrmReceiptCode);
                if (byTaxBlank.HasValue)
                    return byTaxBlank;

                byDocument = await _invoiceService.FindInvoiceIdAsync(imported.OwnedCrmReceiptCode);
                if (byDocument.HasValue)
                    return byDocument;
            }

            if (!string.IsNullOrWhiteSpace(imported.InvoiceNumber))
                return await _invoiceService.FindInvoiceIdByEsfNumberAsync(imported.InvoiceNumber);

            return null;
        }

        private static void EnsureTaxResponseReceipts(IReadOnlyCollection<XElement> receipts)
        {
            var sourceExportCount = receipts.Count(IsSourceExportReceipt);
            if (sourceExportCount == 0)
                return;

            throw new InvalidOperationException(
                sourceExportCount == receipts.Count
                    ? "Выбран исходный XML ЭСФ из выгрузки. Для загрузки нужен ответный XML налоговой; исходный файл повторно загружать нельзя."
                    : "В выбранном файле есть записи исходной выгрузки ЭСФ. Загрузите отдельный ответный XML налоговой без исходных receipt.");
        }

        private static bool IsSourceExportReceipt(XElement receipt)
        {
            var status = ElementValue(receipt, "documentStatusName");
            var hasNewStatus = string.IsNullOrWhiteSpace(status)
                || status.Equals("Новый", StringComparison.OrdinalIgnoreCase)
                || status.Equals("New", StringComparison.OrdinalIgnoreCase);

            if (!hasNewStatus)
                return false;

            var hasGoods = HasElement(receipt, "goods") || receipt.Descendants().Any(item => item.Name.LocalName.Equals("good", StringComparison.OrdinalIgnoreCase));
            var hasExportTotals = HasElement(receipt, "costWithoutTaxes") || HasElement(receipt, "totalCost");
            var hasExportDetails =
                HasElement(receipt, "receiptTypeCode") &&
                HasElement(receipt, "bankAccount") &&
                HasElement(receipt, "contractorPin") &&
                HasElement(receipt, "paymentTypeCode");

            return hasGoods || hasExportTotals || hasExportDetails;
        }

        private static bool HasElement(XElement parent, string name) =>
            parent.Elements().Any(item => item.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static string ElementValue(XElement parent, string name) =>
            parent.Elements().FirstOrDefault(item => item.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim()
            ?? string.Empty;

        private static IEnumerable<XElement> GetReceiptElements(XDocument document)
        {
            if (document.Root == null)
                return Enumerable.Empty<XElement>();

            if (document.Root.Name.LocalName.Equals("receipts", StringComparison.OrdinalIgnoreCase))
                return document.Root.Elements().Where(item => item.Name.LocalName.Equals("receipt", StringComparison.OrdinalIgnoreCase));

            return document.Root
                .Descendants()
                .Where(item => item.Name.LocalName.Equals("receipt", StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateInvoices(
            IReadOnlyCollection<InvoiceDocument> invoices,
            OrganizationEsfInfo primaryOrganization,
            IReadOnlyDictionary<Guid, OrganizationEsfInfo> organizationsById)
        {
            if (string.IsNullOrWhiteSpace(primaryOrganization.Inn))
                throw new InvalidOperationException("У первичной организации не заполнен ИНН.");
            if (string.IsNullOrWhiteSpace(primaryOrganization.BankAccount))
                throw new InvalidOperationException("У первичной организации не заполнен расчетный счет.");

            foreach (var invoice in invoices)
            {
                if (!invoice.OrganizationId.HasValue)
                    throw new InvalidOperationException(
                        $"У счет-фактуры {invoice.DocNumber} не выбрана организация-контрагент.");

                if (!organizationsById.TryGetValue(invoice.OrganizationId.Value, out var counterparty))
                {
                    throw new InvalidOperationException(
                        $"Не найден контрагент по счет-фактуре {invoice.DocNumber}.");
                }

                if (string.IsNullOrWhiteSpace(counterparty.Inn))
                {
                    throw new InvalidOperationException(
                        $"У контрагента счет-фактуры {invoice.DocNumber} не заполнен ИНН.");
                }
            }
        }

        private void EnsureSalesMode()
        {
            if (!InvoiceDocumentTypes.IsSales(_invoiceService.DocumentName))
            {
                throw new InvalidOperationException(
                    "XML-обмен с налоговой сейчас поддержан только для режима 'Выписка счет-фактур'.");
            }
        }

        private static string BuildOwnedCrmReceiptCode(InvoiceDocument invoice)
        {
            if (!string.IsNullOrWhiteSpace(invoice.TaxBlankNumber))
                return invoice.TaxBlankNumber.Trim();
            return MetadataService.NormalizeLegacyDocumentNumber(invoice.DocNumber);
        }

        private static string ResolveReceiptTypeCode()
        {
            return "20";
        }

        private static string ResolvePaymentTypeCode(
            InvoiceDocument invoice,
            IReadOnlyDictionary<string, EsfCatalogEntry> paymentKinds)
        {
            var mapped = ResolveMappedCode(paymentKinds, invoice.PaymentKind, entry => entry.EsfCode);
            if (!string.IsNullOrWhiteSpace(mapped))
                return mapped;

            return invoice.PaymentKind?.Trim().ToUpperInvariant() switch
            {
                "1" or "CASH" => "10",
                "2" or "CARD" => "11",
                "4" or "CHEQUE" => "30",
                _ => "20"
            };
        }

        private static string ResolveInvoiceDeliveryTypeCode(
            InvoiceDocument invoice,
            IReadOnlyDictionary<string, EsfCatalogEntry> deliveryKinds)
        {
            var mapped = ResolveMappedCode(deliveryKinds, invoice.DeliveryKind, entry => entry.EsfCode);
            if (!string.IsNullOrWhiteSpace(mapped))
                return mapped;

            return invoice.DeliveryKind?.Trim().ToUpperInvariant() switch
            {
                "2" or "SERVICE" or "SAMOVIVOZ" => "101",
                "3" or "OTHER" => "299",
                _ => "100"
            };
        }

        private static string ResolveVatDeliveryTypeCode(
            InvoiceDocument invoice,
            IReadOnlyDictionary<string, EsfCatalogEntry> supplyKinds)
        {
            var mapped = ResolveMappedCode(supplyKinds, invoice.SupplyKind, entry => entry.EsfCode);
            if (!string.IsNullOrWhiteSpace(mapped))
                return mapped;

            return invoice.SupplyKind?.Trim().ToUpperInvariant() switch
            {
                "2" or "EXEMPT" or "WITHOUT_TAX" or "NON_TAXABLE_SUPPLY" or "EXEMPT_SUPPLY" or "ZERO_SUPPLY" => "101",
                "3" or "IMP" or "IMPORT" => "200",
                "4" or "EXPORT" => "300",
                _ => "100"
            };
        }

        private static string ResolveVatCode(
            InvoiceDocument invoice,
            IReadOnlyDictionary<string, EsfCatalogEntry> taxesByCode)
        {
            foreach (var line in invoice.Lines)
            {
                var mapped = ResolveMappedCode(taxesByCode, line.VatTaxCode, entry => entry.EsfVatCode);
                if (!string.IsNullOrWhiteSpace(mapped))
                    return mapped;
            }

            return invoice.VatTotal > 0 ? "10" : "90";
        }

        private static string ResolveSalesTaxCode(
            InvoiceLineRow line,
            IReadOnlyDictionary<string, EsfCatalogEntry> taxesByCode)
        {
            var mapped = ResolveMappedCode(taxesByCode, line.SalesTaxCode, entry => entry.EsfSalesTaxCode);
            if (!string.IsNullOrWhiteSpace(mapped))
                return mapped;

            if (int.TryParse(line.SalesTaxCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericCode))
                return numericCode.ToString(CultureInfo.InvariantCulture);
            return "50";
        }

        private static string ResolveMappedCode(
            IReadOnlyDictionary<string, EsfCatalogEntry> entries,
            string? entryCode,
            Func<EsfCatalogEntry, string> selector)
        {
            var normalized = entryCode?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            if (entries.TryGetValue(normalized, out var entry))
                return selector(entry)?.Trim() ?? string.Empty;

            return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericCode)
                ? numericCode.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static XElement Element(EsfXmlTagNames tagNames, string key, string? value) =>
            new(Tag(tagNames, key), value ?? string.Empty);

        private static string Tag(EsfXmlTagNames tagNames, string key) =>
            tagNames.Names.TryGetValue(key, out var tagName) && !string.IsNullOrWhiteSpace(tagName)
                ? tagName
                : key;

        private static Dictionary<string, string> DefaultEsfXmlTagNames() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["VFPDataSet"] = "VFPDataSet",
            ["receipts"] = "receipts",
            ["receipt"] = "receipt",
            ["goods"] = "goods",
            ["good"] = "good",
            ["exchangeCode"] = "exchangeCode",
            ["receiptTypeCode"] = "receiptTypeCode",
            ["createdDate"] = "createdDate",
            ["ownedCrmReceiptCode"] = "ownedCrmReceiptCode",
            ["correctedReceiptCode"] = "correctedReceiptCode",
            ["correctionReasonCode"] = "correctionReasonCode",
            ["bankAccount"] = "bankAccount",
            ["contractorPin"] = "contractorPin",
            ["contractorBankAccount"] = "contractorBankAccount",
            ["deliveryContractNumber"] = "deliveryContractNumber",
            ["deliveryContractDate"] = "deliveryContractDate",
            ["goodsDeliveryTypeCode"] = "goodsDeliveryTypeCode",
            ["paymentTypeCode"] = "paymentTypeCode",
            ["invoiceDeliveryTypeCode"] = "invoiceDeliveryTypeCode",
            ["vatDeliveryTypeCode"] = "vatDeliveryTypeCode",
            ["currencyCode"] = "currencyCode",
            ["exchangeRate"] = "exchangeRate",
            ["contractorCitizenshipCode"] = "contractorCitizenshipCode",
            ["isPriceWithoutTaxes"] = "isPriceWithoutTaxes",
            ["note"] = "note",
            ["vatCode"] = "vatCode",
            ["isResident"] = "isResident",
            ["foreignName"] = "foreignName",
            ["sellerBranchPin"] = "sellerBranchPin",
            ["isIndustry"] = "isIndustry",
            ["openingBalances"] = "openingBalances",
            ["assessedContributionsAmount"] = "assessedContributionsAmount",
            ["paidAmount"] = "paidAmount",
            ["penaltiesAmount"] = "penaltiesAmount",
            ["finesAmount"] = "finesAmount",
            ["closingBalances"] = "closingBalances",
            ["amountToBePaid"] = "amountToBePaid",
            ["personalAccountNumber"] = "personalAccountNumber",
            ["markGoods"] = "markGoods",
            ["invoiceDate"] = "invoiceDate",
            ["invoiceNumber"] = "invoiceNumber",
            ["contractorName"] = "contractorName",
            ["contractorBranchName"] = "contractorBranchName",
            ["currencyName"] = "currencyName",
            ["contractorCitizenshipName"] = "contractorCitizenshipName",
            ["correctedReceiptCreationDate"] = "correctedReceiptCreationDate",
            ["correctionReasonName"] = "correctionReasonName",
            ["documentStatusName"] = "documentStatusName",
            ["correctionSeries"] = "correctionSeries",
            ["type"] = "type",
            ["costWithoutTaxes"] = "costWithoutTaxes",
            ["totalCost"] = "totalCost",
            ["vatAmount"] = "vatAmount",
            ["stCode"] = "stCode",
            ["stAmount"] = "stAmount",
            ["goodsName"] = "goodsName",
            ["baseCount"] = "baseCount",
            ["price"] = "price"
        };

        private static string DecimalText(decimal value, int scale = 2) =>
            Math.Round(value, scale).ToString($"F{scale}", CultureInfo.InvariantCulture);

        private static bool TryGetRowId(IReadOnlyDictionary<string, object> row, out Guid id)
        {
            id = Guid.Empty;
            var raw = GetString(row, "Id");
            return Guid.TryParse(raw, out id);
        }

        private static string GetString(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;
                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            return string.Empty;
        }

        private static bool GetBoolean(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;
                if (value is bool flag)
                    return flag;
                if (bool.TryParse(value.ToString(), out flag))
                    return flag;
            }

            return false;
        }

        private static string BuildUnmatchedDescription(ImportedReceipt imported)
        {
            var key = !string.IsNullOrWhiteSpace(imported.OwnedCrmReceiptCode)
                ? imported.OwnedCrmReceiptCode
                : imported.InvoiceNumber;
            return string.IsNullOrWhiteSpace(key)
                ? "(без локального кода)"
                : $"{key} / {imported.DocumentStatusName}";
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => new UTF8Encoding(false);
        }

        private sealed record PreparedExportBatch(
            string Payload,
            IReadOnlyCollection<PreparedExportInvoice> Invoices,
            DateTime ExportedAt);

        private sealed record EsfReferenceMaps(
            IReadOnlyDictionary<string, EsfCatalogEntry> PaymentKinds,
            IReadOnlyDictionary<string, EsfCatalogEntry> DeliveryKinds,
            IReadOnlyDictionary<string, EsfCatalogEntry> SupplyKinds,
            IReadOnlyDictionary<string, EsfCatalogEntry> Taxes);

        private sealed record EsfXmlTagNames(Dictionary<string, string> Names);

        private sealed record PreparedExportInvoice(Guid InvoiceId, string ExchangeCode, string Status);

        private sealed record EsfCatalogEntry(
            string Code,
            string EsfCode,
            string EsfVatCode,
            string EsfSalesTaxCode,
            string Name);

        private sealed record OrganizationEsfInfo(
            Guid Id,
            string Name,
            string FullName,
            string Inn,
            string Okpo,
            string BankAccount,
            string Address,
            string BankName,
            string Bic,
            string Director,
            string ChiefAccountant,
            bool IsPrimary);

        private sealed record ImportedReceipt(
            string ExchangeCode,
            string InvoiceNumber,
            string OwnedCrmReceiptCode,
            string DocumentStatusName,
            DateTime? CreatedDate,
            DateTime? InvoiceDate)
        {
            public static ImportedReceipt FromXml(XElement receipt)
            {
                static string Value(XElement parent, string name) =>
                    parent.Elements().FirstOrDefault(item => item.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim()
                    ?? string.Empty;

                static DateTime? ParseDate(string value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        return null;
                    if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
                        return dto.DateTime;
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
                        return date;
                    return null;
                }

                return new ImportedReceipt(
                    Value(receipt, "exchangeCode"),
                    Value(receipt, "invoiceNumber"),
                    Value(receipt, "ownedCrmReceiptCode"),
                    Value(receipt, "documentStatusName"),
                    ParseDate(Value(receipt, "createdDate")),
                    ParseDate(Value(receipt, "invoiceDate")));
            }
        }
    }

    public interface ITaxEsfApiClient
    {
        bool IsConfigured { get; }
        Task<TaxEsfApiSubmitResult> SubmitAsync(string xmlPayload, CancellationToken cancellationToken = default);
    }

    public sealed class DisabledTaxEsfApiClient : ITaxEsfApiClient
    {
        public bool IsConfigured => false;

        public Task<TaxEsfApiSubmitResult> SubmitAsync(string xmlPayload, CancellationToken cancellationToken = default)
        {
            _ = xmlPayload;
            _ = cancellationToken;
            throw new InvalidOperationException(
                "API интеграция с налоговой пока не настроена. Используйте ручную выгрузку XML и загрузку ответного файла.");
        }
    }

    public sealed record InvoiceEsfExportResult(int ExportedCount, string OutputPath);
    public sealed record InvoiceEsfImportResult(int TotalReceipts, int UpdatedCount, IReadOnlyCollection<string> UnmatchedReceipts, int SkippedDuplicates = 0);
    public sealed record TaxEsfApiSubmitResult(string TransportName, string RequestId, DateTime SubmittedAt);
}
