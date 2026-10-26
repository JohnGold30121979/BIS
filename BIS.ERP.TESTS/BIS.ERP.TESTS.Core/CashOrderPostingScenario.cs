using System.Globalization;
using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public sealed class CashOrderPostingScenario : SmokeTestScenarioBase
{
    // Номера документов в системе — только цифры; префикс отделяет тестовые данные.
    private const string TestDocumentPrefix = "999800";
    private const string CashOrderDocumentName = "Расходный/Приходный КО";
    private const string CashOrderReceiptKind = "Receipt";
    private const string CashOrderPaymentKind = "Payment";
    private const string CashOrderReceiptPostingType = "Приходный кассовый ордер";
    private const string CashOrderPaymentPostingType = "Расходный кассовый ордер";
    private static readonly string[] TestAccountCodes = ["30100000", "40100000", "60100000", "68100000", "68500000"];
    private const string TestCashDeskName = "Касса (автотест)";
    private const string TestAccountDescription = "Временный счет для smoke-теста проводок кассовых ордеров";

    public override string Code => "cash-order-posting";
    public override string Name => "Касса: проводки ПКО/РКО";
    public override string Category => "Финансы";
    public override string Description => "Проверяет формирование проводок приходных и расходных кассовых ордеров. Тестовые документы остаются в системе, удаляются командой «Очистить».";

    public override async Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        var candidates = await LoadDatabaseCandidatesAsync(settings, progress, cancellationToken);
        var tested = false;
        var errors = new List<string>();
        var details = new List<string>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Report(progress, $"Проверяется база: {candidate.DatabaseName}");
                Report(progress, $"  подключение: {DescribeConnection(candidate)}");
                await using var context = new AppDbContext(candidate.ConnectionString);
                await new RuntimeSchemaFixService(context).EnsureAsync();

                var testContext = await CashOrderTestContext.TryCreateAsync(context, candidate.Name, progress, cancellationToken);
                if (testContext == null)
                    continue;

                tested = true;
                if (options.Command == SmokeTestCommand.Cleanup)
                {
                    var deleted = await CleanupKnownArtifactsAsync(context, testContext, cancellationToken);
                    details.Add($"{candidate.DatabaseName}: удалено тестовых артефактов кассы: {deleted}.");
                    return SmokeTestResult.Success("Очистка тестовых кассовых документов выполнена.", details.ToArray());
                }

                // Документы создаются штатным механизмом и увеличивают счетчики
                // нумерации: снимаем состояние и возвращаем его после проверки.
                var numberingState = await LoadDocumentNumberingStateAsync(context, cancellationToken);
                await RunCashOrderPostingTestsAsync(context, testContext, progress, cancellationToken);
                var restoredCounters = await RestoreDocumentNumberingStateAsync(context, numberingState, cancellationToken);
                details.Add($"{candidate.DatabaseName}: проверены проводки ПКО/РКО, тестовые документы сохранены в системе; " +
                            $"счётчики нумерации восстановлены: {restoredCounters}.");
                return SmokeTestResult.Success("Проверка проводок кассовых ордеров завершена успешно.", details.ToArray());
            }
            catch (Exception ex)
            {
                var message = DescribeError(ex, candidate);
                errors.Add(message);
                Report(progress, $"Ошибка: {message}");
            }
        }

        if (!tested)
            return SmokeTestResult.Failure("Не найдена информационная база с документами ПКО/РКО и справочником План счетов.");

        return SmokeTestResult.Failure("Проверка проводок кассовых ордеров завершилась с ошибкой.", errors);
    }

    private static async Task RunCashOrderPostingTestsAsync(
        AppDbContext context,
        CashOrderTestContext testContext,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var metadataService = new MetadataService(context);
        // Колонка doc_number кассовых ордеров — varchar(20):
        // префикс 999800 + yyMMddHHmmss + номер случая = ровно 20 знаков.
        var runPrefix = DateTime.UtcNow.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);

        var cases = new[]
        {
            new PostingCase(
                Name: "ПКО: поступление оплаты от покупателей",
                Document: testContext.CashOrderDocument,
                OrderKind: CashOrderReceiptKind,
                PostingDocumentType: CashOrderReceiptPostingType,
                Number: $"{TestDocumentPrefix}{runPrefix}01",
                Amount: 15000m,
                CorrespondentAccount: "60100000",
                ExpectedDebit: "30100000",
                ExpectedCredit: "60100000"),
            new PostingCase(
                Name: "РКО: оплата поставщикам",
                Document: testContext.CashOrderDocument,
                OrderKind: CashOrderPaymentKind,
                PostingDocumentType: CashOrderPaymentPostingType,
                Number: $"{TestDocumentPrefix}{runPrefix}02",
                Amount: 8000m,
                CorrespondentAccount: "40100000",
                ExpectedDebit: "40100000",
                ExpectedCredit: "30100000"),
            new PostingCase(
                Name: "РКО: выплата заработной платы",
                Document: testContext.CashOrderDocument,
                OrderKind: CashOrderPaymentKind,
                PostingDocumentType: CashOrderPaymentPostingType,
                Number: $"{TestDocumentPrefix}{runPrefix}03",
                Amount: 25000m,
                CorrespondentAccount: "68100000",
                ExpectedDebit: "68100000",
                ExpectedCredit: "30100000"),
            new PostingCase(
                Name: "РКО: выдача подотчетному лицу",
                Document: testContext.CashOrderDocument,
                OrderKind: CashOrderPaymentKind,
                PostingDocumentType: CashOrderPaymentPostingType,
                Number: $"{TestDocumentPrefix}{runPrefix}04",
                Amount: 5000m,
                CorrespondentAccount: "68500000",
                ExpectedDebit: "68500000",
                ExpectedCredit: "30100000")
        };

        // Тестовые документы и проводки после проверки сохраняются в системе:
        // удаление выполняется только командой «Очистить» (cleanup).
        foreach (var item in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var accountId = testContext.AccountIds[item.CorrespondentAccount];
            var data = BuildDocumentData(item.Document, item.Number, item.OrderKind, item.Amount,
                accountId, item.Name, testContext.CashDeskId);
            var recordId = await metadataService.CreateDynamicRecordAsync(item.Document.Id, data);

            await metadataService.PostDocumentAsync(item.Document.Id, recordId);
            var posting = await LoadPostingAsync(context, item.Number, item.PostingDocumentType, cancellationToken);

            AssertEqual(item.ExpectedDebit, posting.Debit, $"{item.Name}: дебет");
            AssertEqual(item.ExpectedCredit, posting.Credit, $"{item.Name}: кредит");
            AssertEqualDecimal(item.Amount, posting.Amount, $"{item.Name}: сумма");
            AssertAccountCodeFormat(posting.Debit, $"{item.Name}: дебет");
            AssertAccountCodeFormat(posting.Credit, $"{item.Name}: кредит");

            Report(progress, $"OK: {item.Name} -> Дт {posting.Debit} / Кт {posting.Credit}, {posting.Amount:N2}");
        }
    }

    private static Dictionary<string, object> BuildDocumentData(
        MetadataObject document,
        string number,
        string orderKind,
        decimal amount,
        Guid correspondentAccountId,
        string description,
        Guid cashDeskId)
    {
        var data = new Dictionary<string, object>();

        foreach (var field in document.Fields.OrderBy(item => item.Order))
        {
            object? value = null;
            if (IsNumberField(field))
                value = number;
            else if (IsDateField(field))
                value = DateTime.Today;
            else if (IsOrderKindField(field))
                value = orderKind;
            else if (IsAmountField(field))
                value = amount;
            else if (IsDescriptionField(field))
                value = $"Автотест: {description}";
            else if (IsPostedField(field))
                value = false;
            else if (IsCorrespondentAccountField(field))
                value = correspondentAccountId.ToString();
            else if (IsCashDeskField(field))
                value = cashDeskId.ToString();

            if (value != null)
                data[field.Name] = value;
            else if (field.IsRequired)
                data[field.Name] = DefaultValueFor(field);
        }

        data.TryAdd("Номер", number);
        data.TryAdd("Дата", DateTime.Today);
        data.TryAdd("Тип КО", orderKind);
        data.TryAdd("Сумма", amount);
        data.TryAdd("Основание", $"Автотест: {description}");
        data.TryAdd("Проведён", false);
        data.TryAdd("Корр. счет", correspondentAccountId.ToString());
        data.TryAdd("Касса", cashDeskId.ToString());

        return data;
    }

    private static object DefaultValueFor(MetadataField field)
    {
        if (IsNumberField(field))
            return DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        if (IsDateField(field))
            return DateTime.Today;
        if (IsAmountField(field))
            return 0m;
        if (IsPostedField(field))
            return false;
        if (IsCorrespondentAccountField(field) ||
            field.DbColumnName.Contains("account", StringComparison.OrdinalIgnoreCase) ||
            field.Name.Contains("счет", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return field.FieldType switch
        {
            "DateTime" => DateTime.Today,
            "Bool" => false,
            "Int" => 0,
            "Decimal" => 0m,
            _ => $"Автотест {field.Name}"
        };
    }

    private static bool IsNumberField(MetadataField field)
    {
        return field.DbColumnName.Equals("doc_number", StringComparison.OrdinalIgnoreCase) ||
               field.DbColumnName.Equals("number", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Contains("номер", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDateField(MetadataField field)
    {
        return field.DbColumnName.Equals("doc_date", StringComparison.OrdinalIgnoreCase) ||
               field.DbColumnName.Equals("date", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Contains("дата", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOrderKindField(MetadataField field)
    {
        return field.DbColumnName.Equals("order_kind", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Equals("Тип КО", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAmountField(MetadataField field)
    {
        return field.DbColumnName.Equals("amount", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Contains("сумма", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDescriptionField(MetadataField field)
    {
        return field.DbColumnName.Equals("basis", StringComparison.OrdinalIgnoreCase) ||
               field.DbColumnName.Equals("description", StringComparison.OrdinalIgnoreCase) ||
               field.DbColumnName.Equals("purpose", StringComparison.OrdinalIgnoreCase) ||
               field.DbColumnName.Equals("note", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Contains("основан", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Contains("примеч", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Contains("назнач", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPostedField(MetadataField field)
    {
        return field.DbColumnName.Equals("is_posted", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Contains("провед", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCorrespondentAccountField(MetadataField field)
    {
        return field.DbColumnName.Equals("correspondent_account", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Contains("корр", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Поле «Касса» (cash_desk_id). Проверять только точное имя: у поля
    /// «Счет кассы» тоже есть слово «касс», но оно ссылается на план счетов.
    /// </summary>
    private static bool IsCashDeskField(MetadataField field)
    {
        return field.DbColumnName.Equals("cash_desk_id", StringComparison.OrdinalIgnoreCase) ||
               field.Name.Equals("Касса", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<PostingResult> LoadPostingAsync(
        AppDbContext context,
        string number,
        string documentType,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT debit_account, credit_account, amount_kgs
                FROM doc_postings
                WHERE doc_number = @number AND document_type = @type AND is_active = true
                ORDER BY ""CreatedAt"" DESC
                LIMIT 1;";
            command.Parameters.AddWithValue("@number", number);
            command.Parameters.AddWithValue("@type", documentType);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException($"Проводка для документа {documentType} N {number} не найдена.");

            return new PostingResult(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2));
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    private static async Task CleanupAccountsAsync(
        AppDbContext context,
        MetadataObject chartOfAccounts,
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
            return;

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            foreach (var accountId in accountIds)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $@"DELETE FROM {SqlNames.QuoteIdentifier(chartOfAccounts.TableName)} WHERE ""Id"" = @id;";
                command.Parameters.AddWithValue("@id", accountId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    private static async Task<int> CleanupKnownArtifactsAsync(
        AppDbContext context,
        CashOrderTestContext testContext,
        CancellationToken cancellationToken)
    {
        var deleted = 0;
        deleted += await DeleteByNumberPrefixAsync(context, testContext.CashOrderDocument, cancellationToken);
        deleted += await DeleteTestAccountsAsync(context, testContext, cancellationToken);
        deleted += await DeleteTestCashDesksAsync(context, testContext, cancellationToken);
        deleted += await CleanupLegacyInitialPostingArtifactsAsync(context, cancellationToken);
        return deleted;
    }

    /// <summary>
    /// Убирает временные кассы, оставшиеся от прерванных запусков проверки.
    /// </summary>
    private static async Task<int> DeleteTestCashDesksAsync(
        AppDbContext context,
        CashOrderTestContext testContext,
        CancellationToken cancellationToken)
    {
        var sql = "DELETE FROM " + SqlNames.QuoteIdentifier(testContext.CashDeskCatalog.TableName) +
                  " WHERE \"name\" = @name;";
        return await context.Database.ExecuteSqlRawAsync(
            sql,
            [new NpgsqlParameter("@name", TestCashDeskName)],
            cancellationToken);
    }

    /// <summary>
    /// Убирает временные счета, созданные smoke-тестом проводок ПКО/РКО.
    /// </summary>
    private static async Task<int> DeleteTestAccountsAsync(
        AppDbContext context,
        CashOrderTestContext testContext,
        CancellationToken cancellationToken)
    {
        var sql = "DELETE FROM " + SqlNames.QuoteIdentifier(testContext.ChartOfAccounts.TableName) +
                  " WHERE \"description\" = @description;";
        return await context.Database.ExecuteSqlRawAsync(
            sql,
            [new NpgsqlParameter("@description", TestAccountDescription)],
            cancellationToken);
    }

    private static async Task<int> DeleteByNumberPrefixAsync(
        AppDbContext context,
        MetadataObject document,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using (var deletePostings = connection.CreateCommand())
            {
                deletePostings.CommandText = "DELETE FROM doc_postings WHERE doc_number LIKE @prefix AND document_type IN ('Приходный кассовый ордер', 'Расходный кассовый ордер');";
                deletePostings.Parameters.AddWithValue("@prefix", TestDocumentPrefix + "%");
                await deletePostings.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var deleteDocuments = connection.CreateCommand();
            deleteDocuments.CommandText = $@"DELETE FROM {SqlNames.QuoteIdentifier(document.TableName)} WHERE doc_number LIKE @prefix;";
            deleteDocuments.Parameters.AddWithValue("@prefix", TestDocumentPrefix + "%");
            return await deleteDocuments.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    private static async Task<int> CleanupLegacyInitialPostingArtifactsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        return await context.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE
    marker text := 'Тестовая проводка при создании инфобазы';
BEGIN
    IF to_regclass('public.doc_postings') IS NOT NULL
       AND to_regclass('public.doc_cash_orders') IS NOT NULL THEN
        DELETE FROM doc_postings p
        USING doc_cash_orders d
        WHERE p.document_type IN ('Приходный кассовый ордер', 'Расходный кассовый ордер')
          AND p.doc_number = d.doc_number
          AND d.description = marker;

        DELETE FROM doc_cash_orders WHERE description = marker;
    END IF;

    IF to_regclass('public.catalog_test_posting_scenarios') IS NOT NULL THEN
        DROP TABLE catalog_test_posting_scenarios CASCADE;
    END IF;
END $$;", cancellationToken);
    }

    private static async Task<int> RemoveLegacyTestCatalogMetadataAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var catalog = await context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Тестовые сценарии проводок", cancellationToken);

        if (catalog == null)
            return 0;

        if (!string.IsNullOrWhiteSpace(catalog.TableName))
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var opened = false;

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
                opened = true;
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {SqlNames.QuoteIdentifier(catalog.TableName)} CASCADE;";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                if (opened)
                    await connection.CloseAsync();
            }
        }

        context.MetadataFields.RemoveRange(catalog.Fields);
        context.MetadataObjects.Remove(catalog);
        await context.SaveChangesAsync(cancellationToken);
        return 1;
    }

    private static void AssertEqual(string expected, string actual, string subject)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{subject}: ожидалось '{expected}', получено '{actual}'.");
    }

    private static void AssertEqualDecimal(decimal expected, decimal actual, string subject)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{subject}: ожидалось {expected}, получено {actual}.");
    }

    /// <summary>
    /// По ТЗ счета ведутся 8-значными кодами вида 0000.0000 (точка разделяет
    /// группы разрядов); в проводке сохраняются 8 цифр без точки.
    /// </summary>
    private static void AssertAccountCodeFormat(string accountCode, string subject)
    {
        var isValid = accountCode.Length == 8 && accountCode.All(char.IsDigit);
        if (!isValid)
        {
            throw new InvalidOperationException(
                $"{subject}: счёт '{accountCode}' должен быть 8-значным (формат 0000.0000).");
        }
    }

    private sealed record PostingCase(
        string Name,
        MetadataObject Document,
        string Number,
        string OrderKind,
        string PostingDocumentType,
        decimal Amount,
        string CorrespondentAccount,
        string ExpectedDebit,
        string ExpectedCredit);

    private sealed record PostingResult(string Debit, string Credit, decimal Amount);

    private sealed class CashOrderTestContext
    {
        private CashOrderTestContext(
            MetadataObject cashOrderDocument,
            MetadataObject chartOfAccounts,
            IReadOnlyCollection<Guid> createdAccountIds,
            Dictionary<string, Guid> accountIds,
            MetadataObject cashDeskCatalog,
            Guid cashDeskId,
            bool cashDeskCreated)
        {
            CashOrderDocument = cashOrderDocument;
            ChartOfAccounts = chartOfAccounts;
            CreatedAccountIds = createdAccountIds;
            AccountIds = accountIds;
            CashDeskCatalog = cashDeskCatalog;
            CashDeskId = cashDeskId;
            CashDeskCreated = cashDeskCreated;
        }

        public MetadataObject CashOrderDocument { get; }
        public MetadataObject ChartOfAccounts { get; }
        public IReadOnlyCollection<Guid> CreatedAccountIds { get; }
        public Dictionary<string, Guid> AccountIds { get; }
        public MetadataObject CashDeskCatalog { get; }
        public Guid CashDeskId { get; }
        public bool CashDeskCreated { get; }

        public static async Task<CashOrderTestContext?> TryCreateAsync(
            AppDbContext context,
            string candidateName,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            if (!await SmokeTestScenarioBase.HasRequiredTableAsync(context, "MetadataObjects", cancellationToken))
                return null;

            var cashOrder = await context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" &&
                    (item.Name == CashOrderDocumentName || item.TableName == "doc_cash_orders"), cancellationToken);
            var chart = await context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name.StartsWith("План счетов"), cancellationToken);

            if (cashOrder == null || chart == null)
                return null;

            var cashDeskCatalog = await context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" &&
                    (item.Name == "Кассы" || item.TableName == "catalog_cash_desks"), cancellationToken);
            if (cashDeskCatalog == null)
            {
                Report(progress, $"INFO: в базе '{candidateName}' не найден справочник касс — проверка ПКО/РКО пропущена.");
                return null;
            }

            await new MetadataService(context).EnsureCashOrderPostingAccountsAsync();

            var (accounts, createdAccountIds) = await EnsureAccountIdsAsync(context, chart, TestAccountCodes, cancellationToken);
            if (createdAccountIds.Count > 0)
            {
                Report(progress,
                    $"INFO: в базе '{candidateName}' временно созданы счета для smoke-теста: {string.Join(", ", createdAccountIds.Select(id => id.ToString()[..8]))}.");
            }

            var (cashDeskId, cashDeskCreated) = await EnsureCashDeskAsync(context, cashDeskCatalog, cancellationToken);
            if (cashDeskId == Guid.Empty)
            {
                await CleanupAccountsAsync(context, chart, createdAccountIds, cancellationToken);
                Report(progress, $"INFO: в базе '{candidateName}' не удалось подготовить кассу — проверка ПКО/РКО пропущена.");
                return null;
            }

            if (cashDeskCreated)
            {
                Report(progress,
                    $"INFO: в базе '{candidateName}' временно создана касса для smoke-теста: {cashDeskId.ToString()[..8]}.");
            }

            return new CashOrderTestContext(cashOrder, chart, createdAccountIds, accounts, cashDeskCatalog, cashDeskId, cashDeskCreated);
        }

        private static async Task<Dictionary<string, Guid>> LoadAccountIdsAsync(
            AppDbContext context,
            string tableName,
            IReadOnlyCollection<string> accountCodes,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, Guid>();
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var opened = false;

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
                opened = true;
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $@"SELECT ""Id"", ""code"" FROM {SqlNames.QuoteIdentifier(tableName)} WHERE ""code"" = ANY(@codes);";
                command.Parameters.AddWithValue("@codes", accountCodes.ToArray());

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    result[reader.GetString(1)] = reader.GetGuid(0);
            }
            finally
            {
                if (opened)
                    await connection.CloseAsync();
            }

            return result;
        }

        private static async Task<(Dictionary<string, Guid> Accounts, List<Guid> CreatedIds)> EnsureAccountIdsAsync(
            AppDbContext context,
            MetadataObject chart,
            IReadOnlyCollection<string> accountCodes,
            CancellationToken cancellationToken)
        {
            var accounts = await LoadAccountIdsAsync(context, chart.TableName, accountCodes, cancellationToken);
            var createdIds = new List<Guid>();

            foreach (var code in accountCodes.Where(code => !accounts.ContainsKey(code)))
            {
                var accountId = Guid.NewGuid();
                await InsertAccountAsync(context, chart, accountId, code, cancellationToken);
                accounts[code] = accountId;
                createdIds.Add(accountId);
            }

            return (accounts, createdIds);
        }

        private static async Task InsertAccountAsync(
            AppDbContext context,
            MetadataObject chart,
            Guid accountId,
            string code,
            CancellationToken cancellationToken)
        {
            var columns = new List<string> { "\"Id\"", "\"CreatedAt\"", "\"UpdatedAt\"" };
            var values = new List<string> { "@id", "NOW()", "NOW()" };
            var parameters = new List<NpgsqlParameter> { new("@id", accountId) };

            foreach (var field in chart.Fields.OrderBy(item => item.Order))
            {
                if (string.IsNullOrWhiteSpace(field.DbColumnName))
                    continue;

                columns.Add(SqlNames.QuoteIdentifier(field.DbColumnName));
                var parameterName = $"@{field.DbColumnName}";
                values.Add(parameterName);
                parameters.Add(new NpgsqlParameter(parameterName, BuildAccountFieldValue(field, code)));
            }

            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var opened = false;

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
                opened = true;
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $@"
                    INSERT INTO {SqlNames.QuoteIdentifier(chart.TableName)}
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
        }

        private static object BuildAccountFieldValue(MetadataField field, string code)
        {
            return field.DbColumnName switch
            {
                "code" => code,
                "name" => GetAccountName(code),
                "account_type" => GetAccountType(code),
                "description" => TestAccountDescription,
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

        /// <summary>
        /// Возвращает кассу с тестовым счетом. Если подходящей кассы нет,
        /// создает временную и помечает её для удаления после проверки.
        /// </summary>
        private static async Task<(Guid CashDeskId, bool Created)> EnsureCashDeskAsync(
            AppDbContext context,
            MetadataObject cashDeskCatalog,
            CancellationToken cancellationToken)
        {
            var cashAccountCode = TestAccountCodes[0];
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var opened = false;

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
                opened = true;
            }

            try
            {
                await using (var findCommand = connection.CreateCommand())
                {
                    findCommand.CommandText = $@"
                        SELECT ""Id"" FROM {SqlNames.QuoteIdentifier(cashDeskCatalog.TableName)}
                        WHERE ""code"" = @code
                        ORDER BY ""CreatedAt"" DESC
                        LIMIT 1;";
                    findCommand.Parameters.AddWithValue("@code", cashAccountCode);
                    var existing = await findCommand.ExecuteScalarAsync(cancellationToken);
                    if (existing is Guid existingId && existingId != Guid.Empty)
                        return (existingId, false);
                }

                var currencyId = await LoadAnyCurrencyIdAsync(context, connection, cashDeskCatalog, cancellationToken);
                if (currencyId == Guid.Empty)
                    return (Guid.Empty, false);

                var cashDeskId = Guid.NewGuid();
                var columns = new List<string> { "\"Id\"", "\"CreatedAt\"", "\"UpdatedAt\"" };
                var values = new List<string> { "@id", "NOW()", "NOW()" };
                var parameters = new List<NpgsqlParameter> { new("@id", cashDeskId) };

                foreach (var field in cashDeskCatalog.Fields.OrderBy(item => item.Order))
                {
                    if (string.IsNullOrWhiteSpace(field.DbColumnName))
                        continue;

                    columns.Add(SqlNames.QuoteIdentifier(field.DbColumnName));
                    var parameterName = $"@{field.DbColumnName}";
                    values.Add(parameterName);
                    parameters.Add(new NpgsqlParameter(parameterName,
                        BuildCashDeskFieldValue(field, currencyId)));
                }

                await using var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = $@"
                    INSERT INTO {SqlNames.QuoteIdentifier(cashDeskCatalog.TableName)}
                    ({string.Join(", ", columns)})
                    VALUES ({string.Join(", ", values)});";
                insertCommand.Parameters.AddRange(parameters.ToArray());
                await insertCommand.ExecuteNonQueryAsync(cancellationToken);
                return (cashDeskId, true);
            }
            finally
            {
                if (opened)
                    await connection.CloseAsync();
            }
        }

        private static object BuildCashDeskFieldValue(MetadataField field, Guid currencyId)
        {
            return field.DbColumnName switch
            {
                "name" => TestCashDeskName,
                "code" => TestAccountCodes[0],
                "cash_number" => "AUTO",
                "currency_id" => currencyId.ToString(),
                "initial_balance" or "current_balance" => 0m,
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

        /// <summary>
        /// У кассы валюта обязательна: берем валюту существующей кассы,
        /// иначе — первую запись справочника валют.
        /// </summary>
        private static async Task<Guid> LoadAnyCurrencyIdAsync(
            AppDbContext context,
            NpgsqlConnection connection,
            MetadataObject cashDeskCatalog,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var cashDeskCommand = connection.CreateCommand();
                cashDeskCommand.CommandText = $@"
                    SELECT ""currency_id"" FROM {SqlNames.QuoteIdentifier(cashDeskCatalog.TableName)}
                    WHERE ""currency_id"" IS NOT NULL AND ""currency_id"" <> ''
                    LIMIT 1;";
                var cashDeskValue = await cashDeskCommand.ExecuteScalarAsync(cancellationToken);
                if (cashDeskValue != null && Guid.TryParse(cashDeskValue.ToString(), out var existing))
                    return existing;
            }
            catch
            {
                // Структура справочника касс может отличаться — пробуем справочник валют.
            }

            var currenciesCatalog = await context.MetadataObjects
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" &&
                    (item.Name == "Справочник валют" || item.TableName == "catalog_currencies"), cancellationToken);
            if (currenciesCatalog == null || string.IsNullOrWhiteSpace(currenciesCatalog.TableName))
                return Guid.Empty;

            await using var currencyCommand = connection.CreateCommand();
            currencyCommand.CommandText = $@"SELECT ""Id"" FROM {SqlNames.QuoteIdentifier(currenciesCatalog.TableName)} LIMIT 1;";
            var currencyValue = await currencyCommand.ExecuteScalarAsync(cancellationToken);
            if (currencyValue is Guid currencyId && currencyId != Guid.Empty)
                return currencyId;

            return currencyValue != null && Guid.TryParse(currencyValue.ToString(), out var parsed)
                ? parsed
                : Guid.Empty;
        }

        private static string GetAccountName(string code)
        {
            return code switch
            {
                "30100000" => "Касса",
                "40100000" => "Расчеты с поставщиками",
                "60100000" => "Доходы от реализации",
                "68100000" => "Расчеты с персоналом по оплате труда",
                "68500000" => "Расчеты с подотчетными лицами",
                _ => $"Счет {code}"
            };
        }

        private static string GetAccountType(string code)
        {
            return code switch
            {
                "30100000" or "68500000" => "Active",
                _ => "Passive"
            };
        }
    }
}











