using System.Data;
using System.Reflection;
using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public sealed class FinancePostingJournalScenario : SmokeTestScenarioBase
{
    // Номера документов в системе — только цифры; нарочно длиннее 9 цифр,
    // чтобы приложение не «подтягивало» к тестовым номерам счетчики нумерации.
    private const string TestDocumentPrefix = "9999000000";
    private static readonly DateTime TestPostingDate = new(2026, 7, 14);

    /// <summary>Документы, из которых собирается контрольный набор «первички».</summary>
    private static readonly string[] ControlDocumentNames =
    [
        "Расходный/Приходный КО",
        "Платежное поручение",
        "Авансовые платежи",
        "Платежная ведомость",
        "Расчет курсовой разницы",
        "Выписка счет-фактур",
        "Регистрация счет-фактур"
    ];

    private static readonly PostingDocumentExpectation[] PostingDocuments =
    [
        new("Выписка счет-фактур", "Выписка счет-фактур"),
        new("Регистрация счет-фактур", "Регистрация счет-фактур"),
        new("Приходный кассовый ордер", "Расходный/Приходный КО"),
        new("Расходный кассовый ордер", "Расходный/Приходный КО"),
        new("Исходящее платежное поручение", "Платежное поручение"),
        new("Входящее платежное поручение", "Платежное поручение"),
        new("Авансовые платежи", "Авансовые платежи"),
        // Проводки прежних версий хранят тип «Авансовый отчет»: маршрут открытия
        // должен вести к текущему документу «Авансовые платежи».
        new("Авансовый отчет", "Авансовые платежи", JournalPostingCheck.NotExpected),
        new("Платежная ведомость", "Платежная ведомость"),
        // Расчет курсовой разницы создает проводки только при наличии валютных
        // остатков и правил расчета: документ обязателен, проводка — как получится.
        new("Расчет курсовой разницы", "Расчет курсовой разницы", JournalPostingCheck.Optional)
    ];

    public override string Code => "finance-posting-journal";
    public override string Name => "Финансы: центральный журнал проводок";
    public override string Category => "Финансы";
    public override string Description =>
        "Проверяет общий журнал doc_postings, типы финансовых проводок и маршрут открытия исходных документов. " +
        "Контрольный набор собирается из первичных документов: документы создаются и проводятся штатным " +
        "механизмом, и только затем их проводки попадают в общий журнал.";

    public override async Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var details = new List<string>();
        var errors = new List<string>();

        VerifyPostingDocumentRoute(details, errors);

        var candidates = await LoadDatabaseCandidatesAsync(LoadSettings(), progress, cancellationToken);
        var checkedAnyDatabase = false;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var context = new AppDbContext(candidate.ConnectionString);

            if (!await HasRequiredTableAsync(context, "MetadataObjects", cancellationToken))
            {
                details.Add($"{candidate.DatabaseName}: пропущена, это не информационная база BIS ERP.");
                continue;
            }

            checkedAnyDatabase = true;
            Report(progress, $"Проверяется база: {candidate.DatabaseName}");

            try
            {
                await EnsurePostingTableAsync(context, cancellationToken);

                if (options.Command == SmokeTestCommand.Cleanup)
                {
                    var deleted = await CleanupControlDataAsync(context, cancellationToken);
                    details.Add($"{candidate.DatabaseName}: удалено тестовых артефактов журнала: {deleted}.");
                    continue;
                }

                await VerifyMetadataAsync(context, candidate.DatabaseName, details, errors, cancellationToken);

                if (options.Command == SmokeTestCommand.Run)
                {
                    await CleanupControlDataAsync(context, cancellationToken);
                    // Тестовые документы создаются штатным механизмом и увеличивают
                    // счетчики нумерации: снимаем состояние и возвращаем его после проверки.
                    var numberingState = await LoadDocumentNumberingStateAsync(context, cancellationToken);
                    var createdNumbers = await CreateControlDocumentsAsync(context, progress, cancellationToken);
                    var restoredCounters = await RestoreDocumentNumberingStateAsync(context, numberingState, cancellationToken);
                    details.Add($"{candidate.DatabaseName}: создано и проведено первичных документов: " +
                                $"{createdNumbers.Count}; документы остаются в системе, счётчики нумерации восстановлены: {restoredCounters}.");
                }

                await VerifyControlPostingsAsync(context, candidate.DatabaseName, options.Command, details, errors, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate.DatabaseName}: {ex.Message}");
            }
        }

        if (!checkedAnyDatabase)
            errors.Add("Не найдена ни одна информационная база BIS ERP.");

        return errors.Count == 0
            ? SmokeTestResult.Success("Проверка центрального журнала проводок завершена успешно.", details.ToArray())
            : SmokeTestResult.Failure("Проверка центрального журнала проводок выявила ошибки.", errors.Concat(details));
    }

    private static void VerifyPostingDocumentRoute(List<string> details, List<string> errors)
    {
        var openerType = typeof(BIS.ERP.Views.PostingsView).Assembly
            .GetType("BIS.ERP.Views.PostingSourceDocumentOpener");
        var method = openerType?.GetMethod(
            "NormalizeDocumentTypeForLookup",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
        {
            errors.Add("Не найден метод нормализации типа документа для открытия проводок из журнала.");
            return;
        }

        foreach (var expectation in PostingDocuments)
        {
            var normalized = method.Invoke(null, [expectation.PostingDocumentType])?.ToString();
            if (!string.Equals(normalized, expectation.SourceDocumentName, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Маршрут открытия '{expectation.PostingDocumentType}' ведет в '{normalized}', ожидался документ '{expectation.SourceDocumentName}'.");
            }
        }

        details.Add("Маршрут открытия исходного документа из журнала проводок проверен.");
    }

    private static async Task VerifyMetadataAsync(
        AppDbContext context,
        string databaseName,
        List<string> details,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var expectedDocuments = PostingDocuments
            .Select(item => item.SourceDocumentName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var documents = await context.MetadataObjects
            .AsNoTracking()
            .Where(item => item.ObjectType == "Document" && expectedDocuments.Contains(item.Name))
            .Select(item => item.Name)
            .ToListAsync(cancellationToken);

        foreach (var expectedDocument in expectedDocuments)
        {
            if (!documents.Contains(expectedDocument, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{databaseName}: не найден документ '{expectedDocument}'.");
        }

        details.Add($"{databaseName}: проверены метаданные финансовых документов.");
    }

    /// <summary>
    /// Собирает контрольный набор «по-настоящему»: сначала первичные документы
    /// создаются и проводятся штатным механизмом, и только затем их проводки
    /// попадают в общий журнал doc_postings. Прямая вставка проводок в общий
    /// журнал в обход документов не используется.
    /// </summary>
    private static async Task<IReadOnlyCollection<string>> CreateControlDocumentsAsync(
        AppDbContext context,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var created = new List<string>();
        var metadataService = new MetadataService(context);

        var cashDeskId = await LoadCashDeskIdAsync(context, cancellationToken);
        if (cashDeskId == Guid.Empty)
            throw new InvalidOperationException(
                "В базе нет кассы со счетом: заполните справочник «Кассы» для контрольных кассовых ордеров.");

        var employeeId = await LoadFirstCatalogIdAsync(context, "Сотрудники (Списочный состав)", cancellationToken);
        if (employeeId == Guid.Empty)
            throw new InvalidOperationException(
                "В базе нет сотрудников: заполните справочник «Сотрудники (Списочный состав)» для контрольной платежной ведомости.");

        var organizationId = await LoadFirstCatalogIdAsync(context, "Организации", cancellationToken);
        if (organizationId == Guid.Empty)
            throw new InvalidOperationException(
                "В базе нет организаций: заполните справочник «Организации» для контрольных счет-фактур.");

        // Счета — 8-значные коды формата 0000.0000 (без точки), как в ТЗ:
        // это важно для проверок отчетности по счетам.
        var cashDesk = cashDeskId.ToString();
        var plans = new List<ControlDocumentPlan>
        {
            // Касса: поступление денежных средств (ТЗ 1.1.1) — Дт 1110.0000.
            CashReceipt(cashDesk, "32001000", 1200m),
            CashReceipt(cashDesk, "32002000", 800m),
            CashReceipt(cashDesk, "32003000", 5000m),
            CashReceipt(cashDesk, "32004000", 1500m),
            CashReceipt(cashDesk, "32009000", 600m),
            CashReceipt(cashDesk, "31100000", 700m),
            CashReceipt(cashDesk, "36200000", 300m),
            CashReceipt(cashDesk, "33200000", 2000m),
            CashReceipt(cashDesk, "33900000", 1500m),
            CashReceipt(cashDesk, "41300000", 2500m),
            CashReceipt(cashDesk, "41900000", 1800m),
            CashReceipt(cashDesk, "42000000", 5000m),
            CashReceipt(cashDesk, "31200000", 450m),
            CashReceipt(cashDesk, "31300000", 380m),
            CashReceipt(cashDesk, "31400000", 260m),
            CashReceipt(cashDesk, "31900000", 210m),
            CashReceipt(cashDesk, "13300000", 400m),
            CashReceipt(cashDesk, "13900000", 250m),
            CashReceipt(cashDesk, "15500000", 700m),

            // Касса: выплата денежных средств (ТЗ 1.1.2) — Кт 1110.0000.
            CashPayment(cashDesk, "18100000", 900m),
            CashPayment(cashDesk, "18400000", 700m),
            CashPayment(cashDesk, "15100000", 2500m),
            CashPayment(cashDesk, "15200000", 400m),
            CashPayment(cashDesk, "35100000", 300m),
            CashPayment(cashDesk, "35903000", 200m),
            CashPayment(cashDesk, "33200000", 600m),
            CashPayment(cashDesk, "33900000", 450m),
            CashPayment(cashDesk, "35600000", 250m),
            CashPayment(cashDesk, "13300000", 350m),
            CashPayment(cashDesk, "18200000", 500m),
            CashPayment(cashDesk, "18300000", 450m),
            CashPayment(cashDesk, "18900000", 300m),
            CashPayment(cashDesk, "13900000", 200m),
            CashPayment(cashDesk, "36100000", 180m),

            // Банк: поступление на расчетный счет (ТЗ 2.1.1) — Дт 1210.0000.
            BankReceipt("32001000", 3000m),
            BankReceipt("32002000", 1200m),
            BankReceipt("36200000", 500m),
            BankReceipt("33200000", 4000m),
            BankReceipt("41300000", 2200m),
            BankReceipt("41900000", 1700m),
            BankReceipt("32003000", 900m),
            BankReceipt("32004000", 700m),
            BankReceipt("32009000", 500m),
            BankReceipt("31100000", 800m),
            BankReceipt("31200000", 300m),
            BankReceipt("13300000", 350m),
            BankReceipt("13900000", 220m),
            BankReceipt("15500000", 600m),
            BankReceipt("42000000", 3300m),

            // Банк: выбытие с расчетного счета (ТЗ 2.1.2) — Кт 1210.0000.
            BankPayment("18100000", 1400m),
            BankPayment("31100000", 900m),
            BankPayment("35501000", 200m),
            BankPayment("41310000", 150m),
            BankPayment("33900000", 400m),
            BankPayment("41900000", 300m),
            BankPayment("18200000", 400m),
            BankPayment("18300000", 300m),
            BankPayment("18900000", 200m),
            BankPayment("14101000", 350m),
            BankPayment("35100000", 250m),
            BankPayment("35903000", 150m),
            BankPayment("13300000", 100m),
            BankPayment("13900000", 120m),
            BankPayment("35600000", 90m),
            BankPayment("41300000", 260m),
            BankPayment("41200000", 180m),

            // Платежная ведомость (ТЗ 1.1.2.3): Дт 1510.0000 Кт 1110.0000.
            new("Платежная ведомость", new Dictionary<string, object>
            {
                ["Сотрудник"] = employeeId.ToString(),
                ["Дата начала периода"] = new DateTime(TestPostingDate.Year, TestPostingDate.Month, 1),
                ["Дата окончания периода"] = TestPostingDate,
                ["Счет выплаты"] = "11100000",
                ["Счет дебета"] = "15100000",
                ["Счет кредита"] = "11100000",
                ["Сумма"] = 1000m,
                ["Начислено"] = 1000m,
                ["Удержано"] = 0m,
                ["К выплате"] = 1000m
            }),

            // Авансовые платежи (ТЗ 1.1.1.3, авансовый отчет): Дт 7110.0000 Кт 1520.0000.
            new("Авансовые платежи", new Dictionary<string, object>
            {
                ["Сумма"] = 500m,
                ["Принято к учету"] = 500m,
                ["Счет дебета"] = "71100000",
                ["Счет кредита"] = "15200000"
            }),

            // Расчет курсовой разницы (ТЗ раздел 3).
            new("Расчет курсовой разницы", new Dictionary<string, object>
            {
                ["Дата расчета"] = TestPostingDate,
                ["Дата начала периода"] = new DateTime(TestPostingDate.Year, TestPostingDate.Month, 1),
                ["Дата окончания периода"] = TestPostingDate
            })
        };

        var sequence = 0;
        foreach (var plan in plans)
        {
            sequence++;
            var number = $"{TestDocumentPrefix}{sequence:00}";
            await CreateAndPostAsync(context, metadataService, plan.DocumentName, number, plan.Values, progress, cancellationToken);
            created.Add(number);
        }

        // Счета-фактуры с несколькими строками в детали (ТЗ 1.1.1.1 / 1.1.2.1).
        sequence++;
        var salesServiceNumber = $"{TestDocumentPrefix}{sequence:00}";
        await CreateInvoiceAsync(context, "Выписка счет-фактур", salesServiceNumber, organizationId, true, "14101000",
            [("61100000", 1000m), ("61100000", 1500m)],
            progress, cancellationToken);
        created.Add(salesServiceNumber);

        sequence++;
        var salesMaterialNumber = $"{TestDocumentPrefix}{sequence:00}";
        await CreateInvoiceAsync(context, "Выписка счет-фактур", salesMaterialNumber, organizationId, true, "14102000",
            [("62100000", 700m), ("62100000", 400m)],
            progress, cancellationToken);
        created.Add(salesMaterialNumber);

        sequence++;
        var salesRentNumber = $"{TestDocumentPrefix}{sequence:00}";
        await CreateInvoiceAsync(context, "Выписка счет-фактур", salesRentNumber, organizationId, true, "14104000",
            [("62200000", 800m)],
            progress, cancellationToken);
        created.Add(salesRentNumber);

        sequence++;
        var salesOtherNumber = $"{TestDocumentPrefix}{sequence:00}";
        await CreateInvoiceAsync(context, "Выписка счет-фактур", salesOtherNumber, organizationId, true, "14109000",
            [("62900000", 400m)],
            progress, cancellationToken);
        created.Add(salesOtherNumber);

        sequence++;
        var salesAssetNumber = $"{TestDocumentPrefix}{sequence:00}";
        await CreateInvoiceAsync(context, "Выписка счет-фактур", salesAssetNumber, organizationId, true, "14103000",
            [("91901000", 2500m)],
            progress, cancellationToken);
        created.Add(salesAssetNumber);

        sequence++;
        var purchaseMaterialNumber = $"{TestDocumentPrefix}{sequence:00}";
        await CreateInvoiceAsync(context, "Регистрация счет-фактур", purchaseMaterialNumber, organizationId, false, "31100000",
            [("16000000", 800m), ("17000000", 200m), ("21000000", 3000m), ("72900000", 150m)],
            progress, cancellationToken);
        created.Add(purchaseMaterialNumber);

        sequence++;
        var purchaseServiceNumber = $"{TestDocumentPrefix}{sequence:00}";
        await CreateInvoiceAsync(context, "Регистрация счет-фактур", purchaseServiceNumber, organizationId, false, "31400000",
            [("72200000", 600m), ("72900000", 250m)],
            progress, cancellationToken);
        created.Add(purchaseServiceNumber);

        return created;
    }

    private static ControlDocumentPlan CashReceipt(string cashDeskId, string correspondentAccount, decimal amount) =>
        new("Расходный/Приходный КО", new Dictionary<string, object>
        {
            ["Тип КО"] = "Receipt",
            ["Сумма"] = amount,
            ["Касса"] = cashDeskId,
            ["Корр. счет"] = correspondentAccount
        });

    private static ControlDocumentPlan CashPayment(string cashDeskId, string debitAccount, decimal amount) =>
        new("Расходный/Приходный КО", new Dictionary<string, object>
        {
            ["Тип КО"] = "Payment",
            ["Сумма"] = amount,
            ["Касса"] = cashDeskId,
            ["Корр. счет"] = debitAccount
        });

    private static ControlDocumentPlan BankReceipt(string creditAccount, decimal amount) =>
        new("Платежное поручение", new Dictionary<string, object>
        {
            ["Тип"] = "Входящее",
            ["Сумма"] = amount,
            ["Наш счет"] = "12100000",
            ["Корр. счет"] = creditAccount,
            ["Назначение платежа"] = "Автотест: поступление на расчетный счет"
        });

    private static ControlDocumentPlan BankPayment(string debitAccount, decimal amount) =>
        new("Платежное поручение", new Dictionary<string, object>
        {
            ["Тип"] = "Исходящее",
            ["Сумма"] = amount,
            ["Наш счет"] = debitAccount,
            ["Корр. счет"] = "12100000",
            ["Назначение платежа"] = "Автотест: выбытие с расчетного счета"
        });

    private static async Task CreateAndPostAsync(
        AppDbContext context,
        MetadataService metadataService,
        string documentName,
        string number,
        Dictionary<string, object> values,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var document = await LoadDocumentAsync(context, documentName, cancellationToken);
        var fieldNames = document.Fields
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var data = new Dictionary<string, object>();
        foreach (var pair in values)
        {
            if (fieldNames.Contains(pair.Key))
                data[pair.Key] = pair.Value;
        }

        if (fieldNames.Contains("Номер"))
            data["Номер"] = number;
        if (fieldNames.Contains("Дата"))
            data["Дата"] = TestPostingDate;
        if (fieldNames.Contains("Проведён"))
            data["Проведён"] = false;
        if (fieldNames.Contains("Основание"))
            data["Основание"] = "Автотест: контрольный набор журнала проводок";

        foreach (var field in document.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name) || !field.IsRequired || data.ContainsKey(field.Name))
                continue;

            data[field.Name] = RequiredFieldDefault(field);
        }

        var recordId = await metadataService.CreateDynamicRecordAsync(document.Id, data);
        await metadataService.PostDocumentAsync(document.Id, recordId);
        Report(progress, $"{documentName}: создан и проведен документ {number}.");
    }

    private static async Task CreateInvoiceAsync(
        AppDbContext context,
        string documentName,
        string number,
        Guid organizationId,
        bool isSales,
        string counterpartyAccount,
        IReadOnlyList<(string AccountCode, decimal Amount)> lines,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var document = await LoadDocumentAsync(context, documentName, cancellationToken);
        var service = new InvoiceService(context);
        service.Configure(document);
        await service.EnsureSchemaAsync();
        await service.SaveInvoiceAsync(
            BuildInvoiceDocument(number, organizationId, isSales, documentName, counterpartyAccount, lines));
        Report(progress, $"{documentName}: создан и проведен документ {number} (строк в детали: {lines.Count}).");
    }

    /// <summary>
    /// Счет-фактура контрольного набора: несколько строк в детали, счета и налоги —
    /// как в тестовых операциях сценария счет-фактур.
    /// </summary>
    private static InvoiceDocument BuildInvoiceDocument(
        string number,
        Guid organizationId,
        bool isSales,
        string documentName,
        string counterpartyAccount,
        IReadOnlyList<(string AccountCode, decimal Amount)> lines)
    {
        var invoiceLines = new List<InvoiceLineRow>();
        for (var index = 0; index < lines.Count; index++)
        {
            invoiceLines.Add(new InvoiceLineRow
            {
                LineNumber = index + 1,
                Name = $"Автотестовая операция {index + 1}",
                UnitName = "шт",
                Quantity = 1m,
                AccountCode = lines[index].AccountCode,
                VatTaxCode = "НДС12",
                AmountWithoutTax = lines[index].Amount,
                VatRate = 12m,
                SalesTaxCode = "SALES_TAX",
                SalesTaxRate = 1.5m
            });
        }

        return new InvoiceDocument
        {
            DocNumber = number,
            DocDate = TestPostingDate,
            TaxBlankNumber = isSales ? $"AT-{number}" : string.Empty,
            OrganizationId = organizationId,
            CounterpartyAccountCode = counterpartyAccount,
            PaymentKind = "TRANSFER",
            DeliveryKind = "GOODS",
            SupplyKind = "TAXABLE",
            Basis = $"Автотест: {documentName.ToLowerInvariant()}",
            Lines = invoiceLines
        };
    }

    private static object RequiredFieldDefault(MetadataField field)
    {
        return field.FieldType switch
        {
            "Bool" => false,
            "Int" => 0,
            "Decimal" => 0m,
            "DateTime" => TestPostingDate,
            _ => "Автотест"
        };
    }

    private static async Task<MetadataObject> LoadDocumentAsync(
        AppDbContext context,
        string documentName,
        CancellationToken cancellationToken)
    {
        return await context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(
                item => item.ObjectType == "Document" && item.Name == documentName,
                cancellationToken)
            ?? throw new InvalidOperationException($"Документ '{documentName}' не найден в метаданных.");
    }

    private static async Task<Guid> LoadFirstCatalogIdAsync(
        AppDbContext context,
        string catalogName,
        CancellationToken cancellationToken)
    {
        var catalog = await context.MetadataObjects.AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ObjectType == "Catalog" && item.Name == catalogName,
                cancellationToken);
        if (catalog == null || string.IsNullOrWhiteSpace(catalog.TableName))
            return Guid.Empty;

        return await LoadFirstIdAsync(context, catalog.TableName, null, cancellationToken);
    }

    /// <summary>
    /// Касса для контрольных кассовых ордеров: у кассы должен быть заполнен счет,
    /// иначе проведение кассового документа невозможно.
    /// </summary>
    private static async Task<Guid> LoadCashDeskIdAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var catalog = await context.MetadataObjects.AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ObjectType == "Catalog" && item.Name == "Кассы",
                cancellationToken);
        if (catalog == null || string.IsNullOrWhiteSpace(catalog.TableName))
            return Guid.Empty;

        return await LoadFirstIdAsync(
            context,
            catalog.TableName,
            " WHERE \"is_active\" = true AND \"code\" IS NOT NULL AND \"code\" <> ''",
            cancellationToken);
    }

    private static async Task<Guid> LoadFirstIdAsync(
        AppDbContext context,
        string tableName,
        string? extraFilter,
        CancellationToken cancellationToken)
    {
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
            command.CommandText =
                "SELECT \"Id\" FROM " + SqlNames.QuoteIdentifier(tableName) + (extraFilter ?? string.Empty) + " LIMIT 1;";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is Guid id ? id : Guid.Empty;
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    private static async Task VerifyControlPostingsAsync(
        AppDbContext context,
        string databaseName,
        SmokeTestCommand command,
        List<string> details,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var rows = await new PostingService(context).GetAllPostingsAsync(TestPostingDate, TestPostingDate);
        var controlRows = rows
            .Where(row => row.DocumentNumber.StartsWith(TestDocumentPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var exchangeRateRows = rows
            .Where(row =>
                row.DocumentType.Equals("Расчет курсовой разницы", StringComparison.OrdinalIgnoreCase) &&
                row.DocumentNumber.Equals(BuildExchangeRateDocumentNumber(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (controlRows.Count == 0)
        {
            if (command == SmokeTestCommand.Run)
                errors.Add($"{databaseName}: проводки первичных документов не попали в общий журнал.");
            else
                details.Add($"{databaseName}: контрольный набор отсутствует. Для строгой проверки выполните finance-posting-journal run.");
            return;
        }

        foreach (var expectation in PostingDocuments.Where(item => item.Check != JournalPostingCheck.NotExpected))
        {
            var row = controlRows.FirstOrDefault(item =>
                item.DocumentType.Equals(expectation.PostingDocumentType, StringComparison.OrdinalIgnoreCase));

            if (row == null && expectation.Check == JournalPostingCheck.Optional)
            {
                row = exchangeRateRows.FirstOrDefault(item =>
                    item.DocumentType.Equals(expectation.PostingDocumentType, StringComparison.OrdinalIgnoreCase));
            }

            if (row == null)
            {
                if (expectation.Check == JournalPostingCheck.Optional)
                {
                    details.Add($"{databaseName}: проводок типа '{expectation.PostingDocumentType}' пока нет " +
                                "(документ создан и проведен; проводки появятся при наличии валютных остатков).");
                }
                else
                {
                    errors.Add($"{databaseName}: в журнале нет проводки '{expectation.PostingDocumentType}', созданной первичным документом.");
                }

                continue;
            }

            if (!row.ModuleName.Equals("Финансы", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{databaseName}: проводка '{expectation.PostingDocumentType}' показана в модуле '{row.ModuleName}', ожидался модуль 'Финансы'.");
        }

        details.Add($"{databaseName}: общий журнал вернул проводок контрольных документов: {controlRows.Count}.");
    }

    private static async Task EnsurePostingTableAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS doc_postings (
                ""Id"" uuid PRIMARY KEY,
                posting_date timestamp NOT NULL,
                doc_number text NOT NULL,
                document_type text NOT NULL,
                module_code varchar(50),
                debit_account text NOT NULL,
                credit_account text NOT NULL,
                amount_kgs numeric(18,2) NOT NULL DEFAULT 0,
                amount_currency numeric(18,2) NOT NULL DEFAULT 0,
                currency_id text,
                description text,
                is_active boolean NOT NULL DEFAULT true,
                ""CreatedAt"" timestamp NOT NULL DEFAULT NOW(),
                ""UpdatedAt"" timestamp NOT NULL DEFAULT NOW()
            );
            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS module_code varchar(50);
            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS amount_currency numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS currency_id text;
            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS description text;
            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;",
            cancellationToken);
    }

    /// <summary>
    /// Очистка «Очистить»: удаляются только тестовые первичные документы с номерами
    /// 9999000000…, их проводки и проводки расчета курсовой разницы,
    /// созданные этим расчетом.
    /// </summary>
    private static async Task<int> CleanupControlDataAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var deleted = await context.Database.ExecuteSqlRawAsync(
            @"DELETE FROM doc_postings WHERE doc_number LIKE @prefix;",
            [new NpgsqlParameter("@prefix", $"{TestDocumentPrefix}%")],
            cancellationToken);

        deleted += await context.Database.ExecuteSqlRawAsync(
            @"DELETE FROM doc_postings WHERE document_type = @type AND doc_number = @number;",
            [
                new NpgsqlParameter("@type", "Расчет курсовой разницы"),
                new NpgsqlParameter("@number", BuildExchangeRateDocumentNumber())
            ],
            cancellationToken);

        foreach (var documentName in ControlDocumentNames)
        {
            var document = await context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.ObjectType == "Document" && item.Name == documentName,
                    cancellationToken);
            if (document == null || string.IsNullOrWhiteSpace(document.TableName))
                continue;

            var sql = "DELETE FROM " + SqlNames.QuoteIdentifier(document.TableName) +
                      " WHERE doc_number LIKE @prefix;";
            deleted += await context.Database.ExecuteSqlRawAsync(
                sql,
                [new NpgsqlParameter("@prefix", $"{TestDocumentPrefix}%")],
                cancellationToken);
        }

        return deleted;
    }

    /// <summary>
    /// Номер документа расчета курсовой разницы формируется сервисом расчета:
    /// «Курсовая разница yyyyMMdd» (ExchangeRateDifferenceService.BuildDocumentNumber).
    /// </summary>
    private static string BuildExchangeRateDocumentNumber() =>
        $"Курсовая разница {TestPostingDate:yyyyMMdd}";

    private enum JournalPostingCheck
    {
        Required,
        Optional,
        NotExpected
    }

    private sealed record ControlDocumentPlan(string DocumentName, Dictionary<string, object> Values);

    private sealed record PostingDocumentExpectation(
        string PostingDocumentType,
        string SourceDocumentName,
        JournalPostingCheck Check = JournalPostingCheck.Required);
}

