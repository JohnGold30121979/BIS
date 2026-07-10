using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var settings = LoadSettings();
var candidates = await LoadDatabaseCandidatesAsync(settings);

var tested = false;
var errors = new List<string>();

foreach (var candidate in candidates)
{
    try
    {
        await using var context = new AppDbContext(candidate.ConnectionString);
        await new RuntimeSchemaFixService(context).EnsureAsync();
        var testContext = await InvoiceSmokeTestContext.TryCreateAsync(context, candidate.Name);
        if (testContext == null)
            continue;

        tested = true;
        await RunInvoiceSmokeTestsAsync(context, testContext);
        Console.WriteLine($"OK: smoke-тест счет-фактур и ЭСФ выполнен в базе '{candidate.DatabaseName}'.");
        return 0;
    }
    catch (Exception ex)
    {
        errors.Add($"{candidate.DatabaseName}: {ex.Message}");
    }
}

if (!tested)
{
    Console.Error.WriteLine(
        "Не найдена информационная база с документами 'Выписка счет-фактур'/'Регистрация счет-фактур' и каталогом 'Организации'.");
}
else
{
    Console.Error.WriteLine("Smoke-тест счет-фактур завершился с ошибкой:");
}

foreach (var error in errors)
    Console.Error.WriteLine($"- {error}");

return 1;

static async Task RunInvoiceSmokeTestsAsync(AppDbContext context, InvoiceSmokeTestContext testContext)
{
    var runPrefix = DateTime.UtcNow.ToString("MMddHHmmss", CultureInfo.InvariantCulture);
    var createdDocuments = new List<CreatedInvoice>();
    var tempRoot = Path.Combine(Path.GetTempPath(), "BIS.ERP", "InvoiceEsfSmokeTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);

    try
    {
        var salesService = new InvoiceService(context);
        salesService.Configure(testContext.SalesDocument);
        await salesService.EnsureSchemaAsync();

        var purchaseService = new InvoiceService(context);
        purchaseService.Configure(testContext.PurchaseDocument);
        await purchaseService.EnsureSchemaAsync();

        var salesInvoice = new InvoiceDocument
        {
            DocNumber = $"SI{runPrefix}",
            DocDate = DateTime.Today,
            TaxBlankNumber = $"AT-SALE-{runPrefix}",
            OrganizationId = testContext.CounterpartyOrganizationId,
            CounterpartyAccountCode = "14100000",
            PaymentKind = "TRANSFER",
            DeliveryKind = "GOODS",
            SupplyKind = "TAXABLE",
            Basis = "Автотест: выписка счет-фактуры",
            Lines = new List<InvoiceLineRow>
            {
                new()
                {
                    LineNumber = 1,
                    Name = "Автотестовая продажа",
                    UnitName = "шт",
                    Quantity = 1m,
                    AccountCode = "61100000",
                    VatTaxCode = "НДС12",
                    AmountWithoutTax = 1000m,
                    VatRate = 12m,
                    SalesTaxCode = "SALES_TAX",
                    SalesTaxRate = 1.5m
                }
            }
        };

        var salesId = await salesService.SaveInvoiceAsync(salesInvoice);
        createdDocuments.Add(new CreatedInvoice(
            salesId,
            salesInvoice.DocNumber,
            testContext.SalesDocument.Name,
            salesService.HeaderTableName));

        await AssertInvoicePostedAsync(salesService, salesId, "Выписка счет-фактуры");
        var salesPostings = await LoadPostingsAsync(context, salesInvoice.DocNumber, testContext.SalesDocument.Name);
        AssertPostingSet(
            salesPostings,
            new[]
            {
                new ExpectedPosting("14100000", "61100000", 1000m),
                new ExpectedPosting("14100000", "34300000", 120m),
                new ExpectedPosting("14100000", "34900000", 15m)
            },
            "Выписка счет-фактуры");

        var exportService = new InvoiceEsfExchangeService(context, salesService);
        var exportPath = Path.Combine(tempRoot, "sales_export.xml");
        var exportResult = await exportService.ExportSelectedInvoicesAsync(new[] { salesId }, exportPath);
        AssertEqual(1, exportResult.ExportedCount, "Выписка: число выгруженных ЭСФ");
        if (!File.Exists(exportPath))
            throw new InvalidOperationException("Выписка: XML-файл выгрузки не был создан.");

        var exportXml = XDocument.Load(exportPath);
        var receipt = exportXml.Descendants().FirstOrDefault(node => node.Name.LocalName == "receipt")
            ?? throw new InvalidOperationException("Выписка: в XML не найден узел receipt.");
        AssertElementValue(receipt, "receiptTypeCode", "20", "Выписка: receiptTypeCode");
        AssertElementValue(receipt, "ownedCrmReceiptCode", salesInvoice.TaxBlankNumber, "Выписка: ownedCrmReceiptCode");
        AssertElementValue(receipt, "paymentTypeCode", "20", "Выписка: paymentTypeCode");
        AssertElementValue(receipt, "invoiceDeliveryTypeCode", "100", "Выписка: invoiceDeliveryTypeCode");
        AssertElementValue(receipt, "vatDeliveryTypeCode", "100", "Выписка: vatDeliveryTypeCode");
        AssertElementValue(receipt, "vatCode", "10", "Выписка: vatCode");

        var good = receipt.Descendants().FirstOrDefault(node => node.Name.LocalName == "good")
            ?? throw new InvalidOperationException("Выписка: в XML не найден узел good.");
        AssertElementValue(good, "stCode", "50", "Выписка: stCode");
        AssertElementValue(good, "vatAmount", "120.00", "Выписка: vatAmount");
        AssertElementValue(good, "stAmount", "15.00", "Выписка: stAmount");

        var invoiceNumberBeforeImport = GetElementValue(receipt, "invoiceNumber");
        if (!string.IsNullOrWhiteSpace(invoiceNumberBeforeImport))
        {
            throw new InvalidOperationException(
                $"Выписка: до ответа налоговой invoiceNumber должен быть пустым, получено '{invoiceNumberBeforeImport}'.");
        }

        var assignedEsfNumber = $"0002099-999-{DateTime.UtcNow:HHmmss}";
        var responsePath = Path.Combine(tempRoot, "sales_response.xml");
        BuildTaxResponseXml(exportXml, responsePath, assignedEsfNumber, "Принят");

        var importResult = await exportService.ImportResponseAsync(responsePath);
        AssertEqual(1, importResult.TotalReceipts, "Импорт ЭСФ: количество receipt");
        AssertEqual(1, importResult.UpdatedCount, "Импорт ЭСФ: количество обновленных счет-фактур");

        var reloadedSales = await salesService.GetInvoiceAsync(salesId)
            ?? throw new InvalidOperationException("Выписка: документ не найден после импорта ответа налоговой.");
        AssertEqual(assignedEsfNumber, reloadedSales.EsfNumber, "Выписка: номер ЭСФ после импорта");
        AssertEqual("Принят", reloadedSales.TaxStatus, "Выписка: статус ЭСФ после импорта");
        if (string.IsNullOrWhiteSpace(reloadedSales.ExchangeCode))
            throw new InvalidOperationException("Выписка: exchangeCode не заполнен после выгрузки/импорта.");
        if (!reloadedSales.ExportedAt.HasValue)
            throw new InvalidOperationException("Выписка: дата выгрузки не заполнена.");

        var purchaseInvoice = new InvoiceDocument
        {
            DocNumber = $"PI{runPrefix}",
            DocDate = DateTime.Today,
            OrganizationId = testContext.CounterpartyOrganizationId,
            CounterpartyAccountCode = "31100000",
            PaymentKind = "TRANSFER",
            DeliveryKind = "GOODS",
            SupplyKind = "TAXABLE",
            Basis = "Автотест: регистрация счет-фактуры",
            Lines = new List<InvoiceLineRow>
            {
                new()
                {
                    LineNumber = 1,
                    Name = "Автотестовая закупка",
                    UnitName = "шт",
                    Quantity = 1m,
                    AccountCode = "16100000",
                    VatTaxCode = "НДС12",
                    AmountWithoutTax = 2000m,
                    VatRate = 12m,
                    SalesTaxCode = "SALES_TAX",
                    SalesTaxRate = 1.5m
                }
            }
        };

        var purchaseId = await purchaseService.SaveInvoiceAsync(purchaseInvoice);
        createdDocuments.Add(new CreatedInvoice(
            purchaseId,
            purchaseInvoice.DocNumber,
            testContext.PurchaseDocument.Name,
            purchaseService.HeaderTableName));

        await AssertInvoicePostedAsync(purchaseService, purchaseId, "Регистрация счет-фактуры");
        var purchasePostings = await LoadPostingsAsync(context, purchaseInvoice.DocNumber, testContext.PurchaseDocument.Name);
        AssertPostingSet(
            purchasePostings,
            new[]
            {
                new ExpectedPosting("16100000", "31100000", 2000m),
                new ExpectedPosting("15400000", "31100000", 240m),
                new ExpectedPosting("16100000", "31100000", 30m)
            },
            "Регистрация счет-фактуры");

        await purchaseService.UpdateRegistrationInfoAsync(purchaseId, $"BLANK-{runPrefix}", "FIN");
        var reloadedPurchase = await purchaseService.GetInvoiceAsync(purchaseId)
            ?? throw new InvalidOperationException("Регистрация: документ не найден после обновления реквизитов.");
        AssertEqual($"BLANK-{runPrefix}", reloadedPurchase.TaxBlankNumber, "Регистрация: номер бланка");
        AssertEqual("FIN", reloadedPurchase.ModuleCode, "Регистрация: модуль");

        var purchaseExportService = new InvoiceEsfExchangeService(context, purchaseService);
        var purchaseExportAttemptPath = Path.Combine(tempRoot, "purchase_export.xml");
        var purchaseExportBlocked = false;
        try
        {
            await purchaseExportService.ExportSelectedInvoicesAsync(new[] { purchaseId }, purchaseExportAttemptPath);
        }
        catch (InvalidOperationException)
        {
            purchaseExportBlocked = true;
        }

        if (!purchaseExportBlocked)
            throw new InvalidOperationException("Регистрация: XML-выгрузка должна быть недоступна для режима регистрации.");
    }
    finally
    {
        await CleanupInvoicesAsync(context, createdDocuments);
        await CleanupOrganizationsAsync(context, testContext.OrganizationCatalog, testContext.CreatedOrganizationIds);
        DeleteDirectorySafe(tempRoot);
    }
}

static async Task AssertInvoicePostedAsync(InvoiceService service, Guid invoiceId, string caption)
{
    var invoice = await service.GetInvoiceAsync(invoiceId)
        ?? throw new InvalidOperationException($"{caption}: документ не найден после сохранения.");
    if (!invoice.IsPosted)
        throw new InvalidOperationException($"{caption}: документ должен быть проведен сразу после записи.");
}

static void BuildTaxResponseXml(XDocument source, string outputPath, string esfNumber, string status)
{
    var copy = XDocument.Parse(source.ToString(SaveOptions.DisableFormatting));
    foreach (var receipt in copy.Descendants().Where(node => node.Name.LocalName == "receipt"))
    {
        SetElementValue(receipt, "invoiceNumber", esfNumber);
        SetElementValue(receipt, "documentStatusName", status);
        SetElementValue(receipt, "createdDate", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    copy.Save(outputPath);
}

static async Task<List<ActualPosting>> LoadPostingsAsync(
    AppDbContext context,
    string documentNumber,
    string documentType)
{
    var result = new List<ActualPosting>();
    var normalizedNumber = NormalizeLegacyDocumentNumber(documentNumber);
    var connection = (NpgsqlConnection)context.Database.GetDbConnection();
    var opened = false;

    if (connection.State != ConnectionState.Open)
    {
        await connection.OpenAsync();
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

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
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

static void AssertPostingSet(
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

static async Task CleanupInvoicesAsync(AppDbContext context, IReadOnlyCollection<CreatedInvoice> invoices)
{
    if (invoices.Count == 0)
        return;

    var connection = (NpgsqlConnection)context.Database.GetDbConnection();
    var opened = false;

    if (connection.State != ConnectionState.Open)
    {
        await connection.OpenAsync();
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
                await deletePostings.ExecuteNonQueryAsync();
            }

            await using var deleteHeader = connection.CreateCommand();
            deleteHeader.CommandText = $@"
                DELETE FROM {SqlNames.QuoteIdentifier(invoice.HeaderTableName)}
                WHERE ""Id"" = @id;";
            deleteHeader.Parameters.AddWithValue("@id", invoice.Id);
            await deleteHeader.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        if (opened)
            await connection.CloseAsync();
    }
}

static async Task CleanupOrganizationsAsync(
    AppDbContext context,
    MetadataObject organizationCatalog,
    IReadOnlyCollection<Guid> createdOrganizationIds)
{
    if (createdOrganizationIds.Count == 0)
        return;

    var connection = (NpgsqlConnection)context.Database.GetDbConnection();
    var opened = false;

    if (connection.State != ConnectionState.Open)
    {
        await connection.OpenAsync();
        opened = true;
    }

    try
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            DELETE FROM {SqlNames.QuoteIdentifier(organizationCatalog.TableName)}
            WHERE ""Id"" = ANY(@ids);";
        command.Parameters.AddWithValue("@ids", createdOrganizationIds.ToArray());
        await command.ExecuteNonQueryAsync();
    }
    finally
    {
        if (opened)
            await connection.CloseAsync();
    }
}

static void DeleteDirectorySafe(string path)
{
    if (!Directory.Exists(path))
        return;

    try
    {
        Directory.Delete(path, true);
    }
    catch
    {
        // Временные файлы не должны ронять тест.
    }
}

static string GetElementValue(XElement parent, string elementName)
{
    return parent.Elements()
        .FirstOrDefault(item => item.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
        ?.Value?.Trim() ?? string.Empty;
}

static void SetElementValue(XElement parent, string elementName, string value)
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

static void AssertElementValue(XElement parent, string elementName, string expectedValue, string caption)
{
    AssertEqual(expectedValue, GetElementValue(parent, elementName), caption);
}

static void AssertEqual<T>(T expected, T actual, string caption)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{caption}: ожидалось '{expected}', получено '{actual}'.");
}

static string NormalizeLegacyDocumentNumber(string? documentNumber)
{
    if (string.IsNullOrWhiteSpace(documentNumber))
        return string.Empty;

    var normalizedNumber = documentNumber.Trim();
    if (normalizedNumber.Any(char.IsLetter))
        return normalizedNumber;

    var digitsOnly = new string(normalizedNumber.Where(char.IsDigit).ToArray());
    return string.IsNullOrEmpty(digitsOnly) ? normalizedNumber : digitsOnly;
}

static TestSettings LoadSettings()
{
    var searchRoots = new[]
    {
        AppContext.BaseDirectory,
        Directory.GetCurrentDirectory(),
        Path.Combine(Directory.GetCurrentDirectory(), "bin", "Debug", "net8.0-windows")
    };

    foreach (var root in searchRoots.Distinct())
    {
        var path = Path.Combine(root, "appsettings.json");
        if (!File.Exists(path))
            continue;

        var settings = JsonSerializer.Deserialize<TestSettings>(File.ReadAllText(path));
        if (settings != null)
            return settings;
    }

    return new TestSettings();
}

static async Task<List<DatabaseCandidate>> LoadDatabaseCandidatesAsync(TestSettings settings)
{
    var result = new List<DatabaseCandidate>
    {
        new("Текущая база из appsettings", settings.DatabaseName, settings.ConnectionString(settings.DatabaseName))
    };

    try
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString(settings.DatabaseName));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ""Name"", ""DatabaseName"", ""Host"", ""Port"", ""Username"", ""Password""
            FROM ""InfoBases""
            ORDER BY ""IsActive"" DESC, ""CreatedAt"" DESC;";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var databaseName = reader.GetString(1);
            var host = reader.GetString(2);
            var port = reader.GetInt32(3);
            var username = reader.GetString(4);
            var password = reader.GetString(5);
            var connectionString = $"Host={host};Port={port};Database={databaseName};Username={username};Password={password}";

            if (result.All(item => !string.Equals(item.DatabaseName, databaseName, StringComparison.OrdinalIgnoreCase)))
                result.Add(new(name, databaseName, connectionString));
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"INFO: список информационных баз не прочитан: {ex.Message}");
    }

    return result;
}

internal sealed record TestSettings(
    string Host = "localhost",
    int Port = 5432,
    string DatabaseName = "bis_master",
    string Username = "postgres",
    string Password = "qwerty123")
{
    public string ConnectionString(string databaseName)
    {
        return $"Host={Host};Port={Port};Database={databaseName};Username={Username};Password={Password}";
    }
}

internal sealed record DatabaseCandidate(string Name, string DatabaseName, string ConnectionString);

internal sealed record ExpectedPosting(string Debit, string Credit, decimal Amount);

internal sealed record ActualPosting(string Debit, string Credit, decimal Amount, string Description);

internal sealed record CreatedInvoice(Guid Id, string Number, string DocumentType, string HeaderTableName);

internal sealed record OrganizationProbe(Guid Id, string Name, string Inn, string BankAccount, bool IsPrimary);

internal sealed class InvoiceSmokeTestContext
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

    public static async Task<InvoiceSmokeTestContext?> TryCreateAsync(AppDbContext context, string candidateName)
    {
        if (!await HasRequiredTableAsync(context, "MetadataObjects"))
            return null;

        var sales = await context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item =>
                item.ObjectType == "Document" &&
                item.Name == InvoiceDocumentTypes.SalesIssue);
        var purchase = await context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item =>
                item.ObjectType == "Document" &&
                item.Name == InvoiceDocumentTypes.PurchaseRegistration);
        var organizations = await context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item =>
                item.ObjectType == "Catalog" &&
                item.Name == "Организации");

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
                isPrimary: true);
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
                isPrimary: false);
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
        bool isPrimary)
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
            await connection.OpenAsync();
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
            await command.ExecuteNonQueryAsync();
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
        var column = field.DbColumnName;
        return column switch
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

    private static async Task<bool> HasRequiredTableAsync(AppDbContext context, string tableName)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'public' AND table_name = @tableName
                );";
            command.Parameters.AddWithValue("@tableName", tableName);
            return Convert.ToBoolean(await command.ExecuteScalarAsync());
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
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

internal static class SqlNames
{
    public static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
