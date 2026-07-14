using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public sealed class InvoiceEsfSmokeTestScenario : SmokeTestScenarioBase
{
    public override string Code => "invoice-esf";
    public override string Name => "Счет-фактуры и ЭСФ";
    public override string Category => "Финансы";
    public override string Description => "Проверка выписки, регистрации, проводок, XML-выгрузки и обратной загрузки ответа налоговой.";

    private string ManifestPath => Path.Combine(AppContext.BaseDirectory, "invoice-smoke-last-run.json");

    public override async Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return options.Command switch
            {
                SmokeTestCommand.Cleanup => await CleanupSavedRunAsync(progress, cancellationToken),
                SmokeTestCommand.Run => await ExecuteAgainstCandidatesAsync(true, options, progress, cancellationToken),
                _ => await ExecuteAgainstCandidatesAsync(false, options, progress, cancellationToken)
            };
        }
        catch (OperationCanceledException)
        {
            return SmokeTestResult.Failure("Выполнение теста прервано пользователем.");
        }
        catch (Exception ex)
        {
            return SmokeTestResult.Failure($"Тест завершился с ошибкой: {ex.Message}");
        }
    }

    private async Task<SmokeTestResult> ExecuteAgainstCandidatesAsync(
        bool preserveArtifacts,
        SmokeTestRunOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (preserveArtifacts && File.Exists(ManifestPath))
        {
            Report(progress, "Найден след предыдущего тестового запуска, выполняется очистка перед новым run.");
            var cleanup = await CleanupSavedRunAsync(progress, cancellationToken);
            if (!cleanup.IsSuccess)
                return cleanup;
        }

        var settings = LoadSettings();
        var candidates = await LoadDatabaseCandidatesAsync(settings, progress, cancellationToken);
        var tested = false;
        var errors = new List<string>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Report(progress, $"Проверяется база: {candidate.DatabaseName}");
                await using var context = new AppDbContext(candidate.ConnectionString);
                await new RuntimeSchemaFixService(context).EnsureAsync();

                var testContext = await InvoiceSmokeTestContext.TryCreateAsync(context, candidate.Name, cancellationToken);
                if (testContext == null)
                    continue;

                tested = true;
                var artifacts = await RunInvoiceSmokeTestsAsync(
                    context,
                    testContext,
                    options,
                    preserveArtifacts,
                    progress,
                    cancellationToken);

                if (preserveArtifacts)
                {
                    await SaveManifestAsync(
                        new SmokeTestRunManifest(
                            candidate.DatabaseName,
                            candidate.ConnectionString,
                            artifacts.OrganizationCatalogTableName,
                            artifacts.CreatedOrganizationIds.ToList(),
                            artifacts.CreatedInvoices.ToList(),
                            artifacts.TempRoot,
                            DateTime.Now),
                        cancellationToken);

                    Report(progress, $"Тестовые документы оставлены в базе {candidate.DatabaseName}.");
                    return SmokeTestResult.Success(
                        $"Тестовые документы созданы в базе '{candidate.DatabaseName}' и оставлены для проверки.",
                        "Для удаления используйте команду cleanup.");
                }

                return SmokeTestResult.Success(
                    $"Smoke-тест счет-фактур и ЭСФ успешно выполнен в базе '{candidate.DatabaseName}'.");
            }
            catch (Exception ex)
            {
                var message = $"{candidate.DatabaseName}: {ex.Message}";
                errors.Add(message);
                Report(progress, $"Ошибка: {message}");
            }
        }

        if (!tested)
        {
            return SmokeTestResult.Failure(
                "Не найдена информационная база с документами 'Выписка счет-фактур'/'Регистрация счет-фактур' и каталогом 'Организации'.");
        }

        return SmokeTestResult.Failure("Smoke-тест счет-фактур завершился с ошибкой.", errors);
    }

    private async Task<SmokeTestResult> CleanupSavedRunAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(ManifestPath))
            return SmokeTestResult.Success("Сохраненный тестовый запуск не найден, удалять нечего.");

        SmokeTestRunManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SmokeTestRunManifest>(
                await File.ReadAllTextAsync(ManifestPath, cancellationToken));
        }
        catch (Exception ex)
        {
            return SmokeTestResult.Failure(
                $"Не удалось прочитать манифест тестового запуска '{ManifestPath}': {ex.Message}");
        }

        if (manifest == null)
            return SmokeTestResult.Failure($"Манифест тестового запуска '{ManifestPath}' пустой или поврежден.");

        var cleanupSucceeded = false;
        try
        {
            Report(progress, $"Очистка тестовых документов в базе: {manifest.DatabaseName}");
            await using var context = new AppDbContext(manifest.ConnectionString);
            await CleanupInvoicesAsync(context, manifest.CreatedInvoices, cancellationToken);
            await CleanupOrganizationsByTableAsync(
                context,
                manifest.OrganizationCatalogTableName,
                manifest.CreatedOrganizationIds,
                cancellationToken);
            cleanupSucceeded = true;
        }
        catch (Exception ex)
        {
            return SmokeTestResult.Failure($"Ошибка очистки тестовых данных: {ex.Message}");
        }
        finally
        {
            DeleteDirectorySafe(manifest.TempRoot);
            if (cleanupSucceeded)
                DeleteFileSafe(ManifestPath);
        }

        return SmokeTestResult.Success($"Тестовые документы удалены из базы '{manifest.DatabaseName}'.");
    }

    private async Task SaveManifestAsync(SmokeTestRunManifest manifest, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(ManifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            ManifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private async Task<SmokeTestArtifacts> RunInvoiceSmokeTestsAsync(
        AppDbContext context,
        InvoiceSmokeTestContext testContext,
        SmokeTestRunOptions options,
        bool preserveArtifacts,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var runPrefix = DateTime.UtcNow.ToString("MMddHHmmss", CultureInfo.InvariantCulture);
        var createdDocuments = new List<CreatedInvoice>();
        var tempRoot = Path.Combine(Path.GetTempPath(), "BIS.ERP", "InvoiceEsfSmokeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var keepArtifacts = false;

        try
        {
            var salesService = new InvoiceService(context);
            salesService.Configure(testContext.SalesDocument);
            await salesService.EnsureSchemaAsync();

            var purchaseService = new InvoiceService(context);
            purchaseService.Configure(testContext.PurchaseDocument);
            await purchaseService.EnsureSchemaAsync();

            var operations = await ResolveOperationsAsync(options, cancellationToken);
            var salesRuns = new List<PreparedInvoiceRun>();
            var purchaseRuns = new List<PreparedInvoiceRun>();

            var cycleCount = Math.Max(1, options.CycleCount);
            for (var cycle = 1; cycle <= cycleCount; cycle++)
            {
                Report(progress, $"Цикл {cycle}/{cycleCount}: подготовка документов.");
                var sequence = 0;
                foreach (var operation in operations)
                {
                    sequence++;
                    var isSales = operation.DocumentKind.Equals("Sales", StringComparison.OrdinalIgnoreCase);
                    var document = CreateInvoiceDocument(
                        operation,
                        runPrefix,
                        cycle,
                        sequence,
                        testContext.CounterpartyOrganizationId);
                    var service = isSales ? salesService : purchaseService;
                    var documentType = isSales ? testContext.SalesDocument.Name : testContext.PurchaseDocument.Name;

                    Report(progress, $"{(isSales ? "Выписка" : "Регистрация")}: создание документа {document.DocNumber}.");
                    var id = await service.SaveInvoiceAsync(document);
                    createdDocuments.Add(new CreatedInvoice(id, document.DocNumber, documentType, service.HeaderTableName));

                    await AssertInvoicePostedAsync(service, id, isSales ? "Выписка счет-фактуры" : "Регистрация счет-фактуры");
                    var postings = await LoadPostingsAsync(context, document.DocNumber, documentType, cancellationToken);
                    AssertPostingSet(
                        postings,
                        CalculateExpectedPostings(document, isSales),
                        $"{(isSales ? "Выписка" : "Регистрация")} {document.DocNumber}");

                    var prepared = new PreparedInvoiceRun(id, document, operation);
                    if (isSales)
                        salesRuns.Add(prepared);
                    else
                        purchaseRuns.Add(prepared);
                }
            }

            if (salesRuns.Count > 0)
            {
                Report(progress, $"Проверка XML-выгрузки выписки: документов {salesRuns.Count}.");
                var exportService = new InvoiceEsfExchangeService(context, salesService);
                var exportPath = Path.Combine(tempRoot, "sales_export.xml");
                var exportResult = await exportService.ExportSelectedInvoicesAsync(salesRuns.Select(item => item.Id).ToArray(), exportPath);
                AssertEqual(salesRuns.Count, exportResult.ExportedCount, "Выписка: число выгруженных ЭСФ");
                if (!File.Exists(exportPath))
                    throw new InvalidOperationException("Выписка: XML-файл выгрузки не был создан.");

                var exportXml = XDocument.Load(exportPath);
                var receipts = exportXml.Descendants().Where(node => node.Name.LocalName == "receipt").ToList();
                AssertEqual(salesRuns.Count, receipts.Count, "Выписка: количество receipt в XML");

                foreach (var salesRun in salesRuns)
                {
                    var receipt = receipts.FirstOrDefault(node =>
                        GetElementValue(node, "ownedCrmReceiptCode")
                            .Equals(salesRun.Invoice.TaxBlankNumber, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(
                            $"Выписка: в XML не найден receipt для бланка {salesRun.Invoice.TaxBlankNumber}.");

                    AssertReceipt(salesRun.Invoice, receipt);
                }

                var responsePath = Path.Combine(tempRoot, "sales_response.xml");
                var assignedNumbers = salesRuns.ToDictionary(
                    item => item.Invoice.TaxBlankNumber,
                    item => $"0002099-{DateTime.UtcNow:HHmmss}-{item.Invoice.DocNumber}");
                BuildTaxResponseXml(exportXml, responsePath, assignedNumbers, "Принят");

                var importResult = await exportService.ImportResponseAsync(responsePath);
                AssertEqual(salesRuns.Count, importResult.TotalReceipts, "Импорт ЭСФ: количество receipt");
                AssertEqual(salesRuns.Count, importResult.UpdatedCount, "Импорт ЭСФ: количество обновленных счет-фактур");

                foreach (var salesRun in salesRuns)
                {
                    var reloadedSales = await salesService.GetInvoiceAsync(salesRun.Id)
                        ?? throw new InvalidOperationException($"Выписка: документ {salesRun.Invoice.DocNumber} не найден после импорта ответа налоговой.");
                    AssertEqual(assignedNumbers[salesRun.Invoice.TaxBlankNumber], reloadedSales.EsfNumber, $"Выписка {salesRun.Invoice.DocNumber}: номер ЭСФ после импорта");
                    AssertEqual("Принят", reloadedSales.TaxStatus, $"Выписка {salesRun.Invoice.DocNumber}: статус ЭСФ после импорта");
                    if (string.IsNullOrWhiteSpace(reloadedSales.ExchangeCode))
                        throw new InvalidOperationException($"Выписка {salesRun.Invoice.DocNumber}: exchangeCode не заполнен после выгрузки/импорта.");
                    if (!reloadedSales.ExportedAt.HasValue)
                        throw new InvalidOperationException($"Выписка {salesRun.Invoice.DocNumber}: дата выгрузки не заполнена.");
                }
            }

            if (purchaseRuns.Count > 0)
            {
                Report(progress, $"Проверка регистрации счет-фактур: документов {purchaseRuns.Count}.");
                foreach (var purchaseRun in purchaseRuns)
                {
                    var blankNumber = string.IsNullOrWhiteSpace(purchaseRun.Operation.TaxBlankNumber)
                        ? $"BLANK-{purchaseRun.Invoice.DocNumber}"
                        : purchaseRun.Operation.TaxBlankNumber;
                    var moduleCode = string.IsNullOrWhiteSpace(purchaseRun.Operation.ModuleCode)
                        ? "FIN"
                        : purchaseRun.Operation.ModuleCode;

                    await purchaseService.UpdateRegistrationInfoAsync(purchaseRun.Id, blankNumber, moduleCode);
                    var reloadedPurchase = await purchaseService.GetInvoiceAsync(purchaseRun.Id)
                        ?? throw new InvalidOperationException($"Регистрация: документ {purchaseRun.Invoice.DocNumber} не найден после обновления реквизитов.");
                    AssertEqual(blankNumber, reloadedPurchase.TaxBlankNumber, $"Регистрация {purchaseRun.Invoice.DocNumber}: номер бланка");
                    AssertEqual(moduleCode, reloadedPurchase.ModuleCode, $"Регистрация {purchaseRun.Invoice.DocNumber}: модуль");
                }

                Report(progress, "Проверка блокировки XML-выгрузки для регистрации.");
                var purchaseExportService = new InvoiceEsfExchangeService(context, purchaseService);
                var purchaseExportAttemptPath = Path.Combine(tempRoot, "purchase_export.xml");
                var purchaseExportBlocked = false;
                try
                {
                    await purchaseExportService.ExportSelectedInvoicesAsync(purchaseRuns.Select(item => item.Id).ToArray(), purchaseExportAttemptPath);
                }
                catch (InvalidOperationException)
                {
                    purchaseExportBlocked = true;
                }

                if (!purchaseExportBlocked)
                    throw new InvalidOperationException("Регистрация: XML-выгрузка должна быть недоступна для режима регистрации.");
            }

            keepArtifacts = preserveArtifacts;
            return new SmokeTestArtifacts(
                createdDocuments.ToList(),
                testContext.OrganizationCatalog.TableName,
                testContext.CreatedOrganizationIds.ToList(),
                tempRoot);
        }
        finally
        {
            if (!keepArtifacts)
            {
                await CleanupInvoicesAsync(context, createdDocuments, cancellationToken);
                await CleanupOrganizationsByTableAsync(context, testContext.OrganizationCatalog.TableName, testContext.CreatedOrganizationIds, cancellationToken);
                DeleteDirectorySafe(tempRoot);
            }
        }
    }

    private static async Task AssertInvoicePostedAsync(InvoiceService service, Guid invoiceId, string caption)
    {
        var invoice = await service.GetInvoiceAsync(invoiceId)
            ?? throw new InvalidOperationException($"{caption}: документ не найден после сохранения.");
        if (!invoice.IsPosted)
            throw new InvalidOperationException($"{caption}: документ должен быть проведен сразу после записи.");
    }

    private static void BuildTaxResponseXml(
        XDocument source,
        string outputPath,
        IReadOnlyDictionary<string, string> esfNumbersByBlank,
        string status)
    {
        var copy = XDocument.Parse(source.ToString(SaveOptions.DisableFormatting));
        foreach (var receipt in copy.Descendants().Where(node => node.Name.LocalName == "receipt"))
        {
            var blankNumber = GetElementValue(receipt, "ownedCrmReceiptCode");
            if (!esfNumbersByBlank.TryGetValue(blankNumber, out var esfNumber))
                continue;

            SetElementValue(receipt, "invoiceNumber", esfNumber);
            SetElementValue(receipt, "documentStatusName", status);
            SetElementValue(receipt, "createdDate", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        copy.Save(outputPath);
    }

    private static async Task<IReadOnlyCollection<SmokeTestOperation>> ResolveOperationsAsync(
        SmokeTestRunOptions options,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SmokeTestOperation> operations = options.Operations;
        if (operations.Count == 0 && !string.IsNullOrWhiteSpace(options.OperationsFilePath))
        {
            var loader = new OperationImportService();
            operations = await loader.LoadAsync(options.OperationsFilePath, cancellationToken);
        }

        return operations.Count == 0
            ? GenerateDefaultOperations(Math.Max(1, options.DocumentCount))
            : operations;
    }

    private static IReadOnlyCollection<SmokeTestOperation> GenerateDefaultOperations(int documentCount)
    {
        var result = new List<SmokeTestOperation>();
        for (var index = 1; index <= documentCount; index++)
        {
            result.Add(new SmokeTestOperation
            {
                DocumentKind = "Sales",
                Name = $"Автотестовая продажа {index}",
                Quantity = 1m,
                AmountWithoutTax = 1000m + ((index - 1) * 100m),
                VatRate = 12m,
                SalesTaxRate = 1.5m,
                CounterpartyAccountCode = "14100000",
                LineAccountCode = "61100000",
                PaymentKind = "TRANSFER",
                DeliveryKind = "GOODS",
                SupplyKind = "TAXABLE",
                Basis = $"Автотест: выписка счет-фактуры {index}",
                TaxBlankNumber = $"AT-SALE-{index:000}",
                ModuleCode = "FIN"
            });

            result.Add(new SmokeTestOperation
            {
                DocumentKind = "Purchase",
                Name = $"Автотестовая закупка {index}",
                Quantity = 1m,
                AmountWithoutTax = 2000m + ((index - 1) * 100m),
                VatRate = 12m,
                SalesTaxRate = 1.5m,
                CounterpartyAccountCode = "31100000",
                LineAccountCode = "16100000",
                PaymentKind = "TRANSFER",
                DeliveryKind = "GOODS",
                SupplyKind = "TAXABLE",
                Basis = $"Автотест: регистрация счет-фактуры {index}",
                TaxBlankNumber = $"BLANK-{index:000}",
                ModuleCode = "FIN"
            });
        }

        return result;
    }

    private static InvoiceDocument CreateInvoiceDocument(
        SmokeTestOperation operation,
        string runPrefix,
        int cycle,
        int sequence,
        Guid organizationId)
    {
        var isSales = operation.DocumentKind.Equals("Sales", StringComparison.OrdinalIgnoreCase);
        var docPrefix = isSales ? "SI" : "PI";
        var fallbackCounterparty = isSales ? "14100000" : "31100000";
        var fallbackLine = isSales ? "61100000" : "16100000";
        var normalizedBlank = string.IsNullOrWhiteSpace(operation.TaxBlankNumber)
            ? $"{(isSales ? "AT-SALE" : "BLANK")}-{runPrefix}-{cycle:00}-{sequence:000}"
            : $"{operation.TaxBlankNumber}-{cycle:00}-{sequence:000}";

        return new InvoiceDocument
        {
            DocNumber = $"{docPrefix}{runPrefix}{cycle:00}{sequence:000}",
            DocDate = DateTime.Today,
            TaxBlankNumber = isSales ? normalizedBlank : string.Empty,
            OrganizationId = organizationId,
            CounterpartyAccountCode = string.IsNullOrWhiteSpace(operation.CounterpartyAccountCode)
                ? fallbackCounterparty
                : operation.CounterpartyAccountCode,
            PaymentKind = string.IsNullOrWhiteSpace(operation.PaymentKind) ? "TRANSFER" : operation.PaymentKind,
            DeliveryKind = string.IsNullOrWhiteSpace(operation.DeliveryKind) ? "GOODS" : operation.DeliveryKind,
            SupplyKind = string.IsNullOrWhiteSpace(operation.SupplyKind) ? "TAXABLE" : operation.SupplyKind,
            Basis = string.IsNullOrWhiteSpace(operation.Basis)
                ? (isSales ? "Автотест: выписка счет-фактуры" : "Автотест: регистрация счет-фактуры")
                : operation.Basis,
            Lines = new List<InvoiceLineRow>
            {
                new()
                {
                    LineNumber = 1,
                    Name = string.IsNullOrWhiteSpace(operation.Name) ? "Автотестовая операция" : operation.Name,
                    UnitName = "шт",
                    Quantity = operation.Quantity <= 0 ? 1m : operation.Quantity,
                    AccountCode = string.IsNullOrWhiteSpace(operation.LineAccountCode) ? fallbackLine : operation.LineAccountCode,
                    VatTaxCode = "НДС12",
                    AmountWithoutTax = operation.AmountWithoutTax <= 0 ? 1000m : operation.AmountWithoutTax,
                    VatRate = operation.VatRate < 0 ? 0m : operation.VatRate,
                    SalesTaxCode = "SALES_TAX",
                    SalesTaxRate = operation.SalesTaxRate < 0 ? 0m : operation.SalesTaxRate
                }
            }
        };
    }

    private static IReadOnlyCollection<ExpectedPosting> CalculateExpectedPostings(InvoiceDocument document, bool isSales)
    {
        var line = document.Lines.Single();
        InvoiceService.RecalculateLine(line);

        var lineAccount = string.IsNullOrWhiteSpace(line.AccountCode)
            ? (isSales ? "61100000" : "16100000")
            : line.AccountCode.Trim();
        var counterpartyAccount = string.IsNullOrWhiteSpace(document.CounterpartyAccountCode)
            ? (isSales ? "14100000" : "31100000")
            : document.CounterpartyAccountCode.Trim();

        var result = new List<ExpectedPosting>();
        if (line.AmountWithoutTax > 0)
        {
            result.Add(isSales
                ? new ExpectedPosting(counterpartyAccount, lineAccount, line.AmountWithoutTax)
                : new ExpectedPosting(lineAccount, counterpartyAccount, line.AmountWithoutTax));
        }

        if (line.VatAmount > 0)
        {
            result.Add(isSales
                ? new ExpectedPosting(counterpartyAccount, "34300000", line.VatAmount)
                : new ExpectedPosting("15400000", counterpartyAccount, line.VatAmount));
        }

        if (line.SalesTaxAmount > 0)
        {
            result.Add(isSales
                ? new ExpectedPosting(counterpartyAccount, "34004000", line.SalesTaxAmount)
                : new ExpectedPosting(lineAccount, counterpartyAccount, line.SalesTaxAmount));
        }

        return result;
    }

    private static void AssertReceipt(InvoiceDocument invoice, XElement receipt)
    {
        AssertElementNotEmpty(receipt, "receiptTypeCode", $"Выписка {invoice.DocNumber}: receiptTypeCode");
        AssertElementValue(receipt, "ownedCrmReceiptCode", invoice.TaxBlankNumber, $"Выписка {invoice.DocNumber}: ownedCrmReceiptCode");
        AssertElementNotEmpty(receipt, "paymentTypeCode", $"Выписка {invoice.DocNumber}: paymentTypeCode");
        AssertElementNotEmpty(receipt, "invoiceDeliveryTypeCode", $"Выписка {invoice.DocNumber}: invoiceDeliveryTypeCode");
        AssertElementNotEmpty(receipt, "vatDeliveryTypeCode", $"Выписка {invoice.DocNumber}: vatDeliveryTypeCode");
        AssertElementNotEmpty(receipt, "vatCode", $"Выписка {invoice.DocNumber}: vatCode");

        var good = receipt.Descendants().FirstOrDefault(node => node.Name.LocalName == "good")
            ?? throw new InvalidOperationException($"Выписка {invoice.DocNumber}: в XML не найден узел good.");
        var line = invoice.Lines.Single();
        InvoiceService.RecalculateLine(line);
        AssertElementValue(good, "vatAmount", line.VatAmount.ToString("0.00", CultureInfo.InvariantCulture), $"Выписка {invoice.DocNumber}: vatAmount");
        AssertElementValue(good, "stAmount", line.SalesTaxAmount.ToString("0.00", CultureInfo.InvariantCulture), $"Выписка {invoice.DocNumber}: stAmount");

        var invoiceNumberBeforeImport = GetElementValue(receipt, "invoiceNumber");
        if (!string.IsNullOrWhiteSpace(invoiceNumberBeforeImport))
        {
            throw new InvalidOperationException(
                $"Выписка {invoice.DocNumber}: до ответа налоговой invoiceNumber должен быть пустым, получено '{invoiceNumberBeforeImport}'.");
        }
    }

    private static void AssertElementNotEmpty(XElement parent, string elementName, string caption)
    {
        var value = GetElementValue(parent, elementName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{caption}: значение не заполнено.");
    }

    private static async Task<List<ActualPosting>> LoadPostingsAsync(
        AppDbContext context,
        string documentNumber,
        string documentType,
        CancellationToken cancellationToken)
    {
        var result = new List<ActualPosting>();
        var normalizedNumber = NormalizeLegacyDocumentNumber(documentNumber);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT debit_account, credit_account, amount_kgs, description
                FROM doc_postings
                WHERE (doc_number = @number OR doc_number = @normalizedNumber)
                  AND document_type = @type
                  AND is_active = true
                ORDER BY ""CreatedAt"", debit_account, credit_account;";
            command.Parameters.AddWithValue("@number", documentNumber);
            command.Parameters.AddWithValue("@normalizedNumber", normalizedNumber);
            command.Parameters.AddWithValue("@type", documentType);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new ActualPosting(
                    reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
            }
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }

        return result;
    }

    private static void AssertPostingSet(
        IReadOnlyCollection<ActualPosting> actual,
        IReadOnlyCollection<ExpectedPosting> expected,
        string caption)
    {
        if (actual.Count != expected.Count)
        {
            throw new InvalidOperationException(
                $"{caption}: ожидалось проводок {expected.Count}, получено {actual.Count}. " +
                $"Фактически: {string.Join("; ", actual.Select(item => $"{item.Debit}/{item.Credit}/{item.Amount:N2}"))}");
        }

        var unmatched = actual
            .Select(item => new ExpectedPosting(item.Debit, item.Credit, item.Amount))
            .ToList();

        foreach (var expectedPosting in expected)
        {
            var match = unmatched.FindIndex(item =>
                item.Debit == expectedPosting.Debit &&
                item.Credit == expectedPosting.Credit &&
                item.Amount == expectedPosting.Amount);

            if (match < 0)
            {
                throw new InvalidOperationException(
                    $"{caption}: не найдена ожидаемая проводка {expectedPosting.Debit}/{expectedPosting.Credit}/{expectedPosting.Amount:N2}.");
            }

            unmatched.RemoveAt(match);
        }

        if (unmatched.Count > 0)
        {
            throw new InvalidOperationException(
                $"{caption}: найдены лишние проводки {string.Join("; ", unmatched.Select(item => $"{item.Debit}/{item.Credit}/{item.Amount:N2}"))}.");
        }
    }

    private static async Task CleanupInvoicesAsync(
        AppDbContext context,
        IReadOnlyCollection<CreatedInvoice> invoices,
        CancellationToken cancellationToken)
    {
        if (invoices.Count == 0)
            return;

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            foreach (var invoice in invoices)
            {
                await using (var deletePostings = connection.CreateCommand())
                {
                    deletePostings.CommandText = @"
                        DELETE FROM doc_postings
                        WHERE doc_number = @number AND document_type = @type;";
                    deletePostings.Parameters.AddWithValue("@number", invoice.Number);
                    deletePostings.Parameters.AddWithValue("@type", invoice.DocumentType);
                    await deletePostings.ExecuteNonQueryAsync(cancellationToken);
                }

                await using var deleteHeader = connection.CreateCommand();
                deleteHeader.CommandText = $@"
                    DELETE FROM {SqlNames.QuoteIdentifier(invoice.HeaderTableName)}
                    WHERE ""Id"" = @id;";
                deleteHeader.Parameters.AddWithValue("@id", invoice.Id);
                await deleteHeader.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    private static async Task CleanupOrganizationsByTableAsync(
        AppDbContext context,
        string organizationCatalogTableName,
        IReadOnlyCollection<Guid> createdOrganizationIds,
        CancellationToken cancellationToken)
    {
        if (createdOrganizationIds.Count == 0)
            return;

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                DELETE FROM {SqlNames.QuoteIdentifier(organizationCatalogTableName)}
                WHERE ""Id"" = ANY(@ids);";
            command.Parameters.AddWithValue("@ids", createdOrganizationIds.ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    private static string GetElementValue(XElement parent, string elementName)
    {
        return parent.Elements()
            .FirstOrDefault(item => item.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim() ?? string.Empty;
    }

    private static void SetElementValue(XElement parent, string elementName, string value)
    {
        var element = parent.Elements()
            .FirstOrDefault(item => item.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase));
        if (element == null)
        {
            parent.Add(new XElement(elementName, value));
            return;
        }

        element.Value = value;
    }

    private static void AssertElementValue(XElement parent, string elementName, string expectedValue, string caption)
    {
        AssertEqual(expectedValue, GetElementValue(parent, elementName), caption);
    }

    private static void AssertEqual<T>(T expected, T actual, string caption)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{caption}: ожидалось '{expected}', получено '{actual}'.");
    }

    private sealed record ExpectedPosting(string Debit, string Credit, decimal Amount);
    private sealed record ActualPosting(string Debit, string Credit, decimal Amount, string Description);
    private sealed record CreatedInvoice(Guid Id, string Number, string DocumentType, string HeaderTableName);
    private sealed record PreparedInvoiceRun(Guid Id, InvoiceDocument Invoice, SmokeTestOperation Operation);

    private sealed record SmokeTestArtifacts(
        IReadOnlyCollection<CreatedInvoice> CreatedInvoices,
        string OrganizationCatalogTableName,
        IReadOnlyCollection<Guid> CreatedOrganizationIds,
        string TempRoot);

    private sealed record SmokeTestRunManifest(
        string DatabaseName,
        string ConnectionString,
        string OrganizationCatalogTableName,
        List<Guid> CreatedOrganizationIds,
        List<CreatedInvoice> CreatedInvoices,
        string TempRoot,
        DateTime CreatedAt);

    private sealed record OrganizationProbe(Guid Id, string Name, string Inn, string BankAccount, bool IsPrimary);

    private sealed class InvoiceSmokeTestContext
    {
        private InvoiceSmokeTestContext(
            MetadataObject salesDocument,
            MetadataObject purchaseDocument,
            MetadataObject organizationCatalog,
            Guid primaryOrganizationId,
            Guid counterpartyOrganizationId,
            IReadOnlyCollection<Guid> createdOrganizationIds)
        {
            SalesDocument = salesDocument;
            PurchaseDocument = purchaseDocument;
            OrganizationCatalog = organizationCatalog;
            PrimaryOrganizationId = primaryOrganizationId;
            CounterpartyOrganizationId = counterpartyOrganizationId;
            CreatedOrganizationIds = createdOrganizationIds;
        }

        public MetadataObject SalesDocument { get; }
        public MetadataObject PurchaseDocument { get; }
        public MetadataObject OrganizationCatalog { get; }
        public Guid PrimaryOrganizationId { get; }
        public Guid CounterpartyOrganizationId { get; }
        public IReadOnlyCollection<Guid> CreatedOrganizationIds { get; }

        public static async Task<InvoiceSmokeTestContext?> TryCreateAsync(
            AppDbContext context,
            string candidateName,
            CancellationToken cancellationToken)
        {
            if (!await SmokeTestScenarioBase.HasRequiredTableAsync(context, "MetadataObjects", cancellationToken))
                return null;

            var sales = await context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Document" &&
                    item.Name == InvoiceDocumentTypes.SalesIssue, cancellationToken);
            var purchase = await context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Document" &&
                    item.Name == InvoiceDocumentTypes.PurchaseRegistration, cancellationToken);
            var organizations = await context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Catalog" &&
                    item.Name == "Организации", cancellationToken);

            if (sales == null || purchase == null || organizations == null)
                return null;

            var createdOrganizationIds = new List<Guid>();
            var metadataService = new MetadataService(context);
            var existingRows = await metadataService.GetCatalogDataAsync(organizations.Id);
            var organizationList = existingRows
                .Select(ToOrganizationProbe)
                .Where(item => item.Id != Guid.Empty)
                .ToList();

            var primary = organizationList.FirstOrDefault(item =>
                item.IsPrimary &&
                !string.IsNullOrWhiteSpace(item.Inn) &&
                !string.IsNullOrWhiteSpace(item.BankAccount));
            primary ??= organizationList.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.Inn) &&
                !string.IsNullOrWhiteSpace(item.BankAccount));

            if (primary == null)
            {
                primary = await CreateOrganizationAsync(
                    context,
                    organizations,
                    $"{candidateName}-Issuer",
                    $"AUTO_ISS_{DateTime.UtcNow:MMddHHmmss}",
                    true,
                    cancellationToken);
                createdOrganizationIds.Add(primary.Id);
            }

            var counterparty = organizationList.FirstOrDefault(item =>
                item.Id != primary.Id &&
                !string.IsNullOrWhiteSpace(item.Inn));

            if (counterparty == null)
            {
                counterparty = await CreateOrganizationAsync(
                    context,
                    organizations,
                    $"{candidateName}-Counterparty",
                    $"AUTO_CNT_{DateTime.UtcNow:MMddHHmmss}",
                    false,
                    cancellationToken);
                createdOrganizationIds.Add(counterparty.Id);
            }

            return new InvoiceSmokeTestContext(
                sales,
                purchase,
                organizations,
                primary.Id,
                counterparty.Id,
                createdOrganizationIds);
        }

        private static OrganizationProbe ToOrganizationProbe(Dictionary<string, object> row)
        {
            var idText = GetString(row, "Id");
            return new OrganizationProbe(
                Guid.TryParse(idText, out var id) ? id : Guid.Empty,
                GetString(row, "Наименование", "name", "Полное наименование", "full_name"),
                GetString(row, "ИНН", "inn"),
                GetString(row, "Расчетный счет", "bank_account"),
                GetBoolean(row, "Первичная организация", "is_primary"));
        }

        private static async Task<OrganizationProbe> CreateOrganizationAsync(
            AppDbContext context,
            MetadataObject organizationCatalog,
            string name,
            string code,
            bool isPrimary,
            CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid();
            var inn = isPrimary ? "12345678901234" : "43210987654321";
            var bankAccount = isPrimary ? "1280010000028031" : "1360373001673071";
            var columns = new List<string> { "\"Id\"", "\"CreatedAt\"", "\"UpdatedAt\"" };
            var values = new List<string> { "@id", "NOW()", "NOW()" };
            var parameters = new List<NpgsqlParameter> { new("@id", id) };

            foreach (var field in organizationCatalog.Fields.OrderBy(item => item.Order))
            {
                if (string.IsNullOrWhiteSpace(field.DbColumnName))
                    continue;

                columns.Add(SqlNames.QuoteIdentifier(field.DbColumnName));
                var parameterName = $"@{field.DbColumnName}";
                values.Add(parameterName);
                parameters.Add(new NpgsqlParameter(parameterName, BuildOrganizationFieldValue(field, code, name, inn, bankAccount, isPrimary)));
            }

            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var opened = false;
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
                opened = true;
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $@"
                    INSERT INTO {SqlNames.QuoteIdentifier(organizationCatalog.TableName)}
                    ({string.Join(", ", columns)})
                    VALUES ({string.Join(", ", values)});";
                command.Parameters.AddRange(parameters.ToArray());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                if (opened)
                    await connection.CloseAsync();
            }

            return new OrganizationProbe(id, name, inn, bankAccount, isPrimary);
        }

        private static object BuildOrganizationFieldValue(
            MetadataField field,
            string code,
            string name,
            string inn,
            string bankAccount,
            bool isPrimary)
        {
            return field.DbColumnName switch
            {
                "code" => code,
                "name" => name,
                "full_name" => $"{name} (автотест)",
                "description" => "Временная организация для smoke-теста счет-фактур",
                "inn" => inn,
                "okpo" => "12345678",
                "bank_account" => bankAccount,
                "legal_address" => "г. Бишкек, ул. Тестовая, 1",
                "actual_address" => "г. Бишкек, ул. Тестовая, 1",
                "bank_name" => "Автотест Банк",
                "bic" => "TESTKG22",
                "director" => "Автотест Директор",
                "chief_accountant" => "Автотест Бухгалтер",
                "is_primary" => isPrimary,
                "is_active" => true,
                _ => field.FieldType switch
                {
                    "Bool" => false,
                    "Int" => 0,
                    "Decimal" => 0m,
                    "DateTime" => DateTime.Today,
                    _ => string.Empty
                }
            };
        }

        private static string GetString(Dictionary<string, object> row, params string[] keys)
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

        private static bool GetBoolean(Dictionary<string, object> row, params string[] keys)
        {
            var value = GetString(row, keys);
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("да", StringComparison.OrdinalIgnoreCase);
        }
    }
}
