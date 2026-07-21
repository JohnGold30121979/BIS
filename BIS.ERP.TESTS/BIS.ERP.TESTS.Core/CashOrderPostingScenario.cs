using System.Globalization;
using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public sealed class CashOrderPostingScenario : SmokeTestScenarioBase
{
    private const string TestDocumentPrefix = "TEST-CASH-ORDER-";
    private const string CashOrderDocumentName = "Расходный/Приходный КО";
    private const string CashOrderReceiptKind = "Receipt";
    private const string CashOrderPaymentKind = "Payment";
    private const string CashOrderReceiptPostingType = "Приходный кассовый ордер";
    private const string CashOrderPaymentPostingType = "Расходный кассовый ордер";
    private static readonly string[] TestAccountCodes = ["3010", "4010", "6010", "6810", "6850"];

    public override string Code => "cash-order-posting";
    public override string Name => "Касса: проводки ПКО/РКО";
    public override string Category => "Финансы";
    public override string Description => "Проверяет формирование проводок приходных и расходных кассовых ордеров. Тестовые документы удаляются после проверки.";

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

                await RunCashOrderPostingTestsAsync(context, testContext, progress, cancellationToken);
                details.Add($"{candidate.DatabaseName}: проверены проводки ПКО/РКО, тестовые документы удалены.");
                return SmokeTestResult.Success("Проверка проводок кассовых ордеров завершена успешно.", details.ToArray());
            }
            catch (Exception ex)
            {
                var message = $"{candidate.DatabaseName}: {ex.Message}";
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
        var runPrefix = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var createdRecords = new List<(MetadataObject Document, Guid RecordId, string Number, string PostingDocumentType)>();

        var cases = new[]
        {
            new PostingCase(
                Name: "ПКО: поступление оплаты от покупателей",
                Document: testContext.CashOrderDocument,
                OrderKind: CashOrderReceiptKind,
                PostingDocumentType: CashOrderReceiptPostingType,
                Number: $"{TestDocumentPrefix}{runPrefix}-01",
                Amount: 15000m,
                CorrespondentAccount: "6010",
                ExpectedDebit: "3010",
                ExpectedCredit: "6010"),
            new PostingCase(
                Name: "РКО: оплата поставщикам",
                Document: testContext.CashOrderDocument,
                OrderKind: CashOrderPaymentKind,
                PostingDocumentType: CashOrderPaymentPostingType,
                Number: $"{TestDocumentPrefix}{runPrefix}-02",
                Amount: 8000m,
                CorrespondentAccount: "4010",
                ExpectedDebit: "4010",
                ExpectedCredit: "3010"),
            new PostingCase(
                Name: "РКО: выплата заработной платы",
                Document: testContext.CashOrderDocument,
                OrderKind: CashOrderPaymentKind,
                PostingDocumentType: CashOrderPaymentPostingType,
                Number: $"{TestDocumentPrefix}{runPrefix}-03",
                Amount: 25000m,
                CorrespondentAccount: "6810",
                ExpectedDebit: "6810",
                ExpectedCredit: "3010"),
            new PostingCase(
                Name: "РКО: выдача подотчетному лицу",
                Document: testContext.CashOrderDocument,
                OrderKind: CashOrderPaymentKind,
                PostingDocumentType: CashOrderPaymentPostingType,
                Number: $"{TestDocumentPrefix}{runPrefix}-04",
                Amount: 5000m,
                CorrespondentAccount: "6850",
                ExpectedDebit: "6850",
                ExpectedCredit: "3010")
        };

        try
        {
            foreach (var item in cases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var accountId = testContext.AccountIds[item.CorrespondentAccount];
                var data = BuildDocumentData(item.Document, item.Number, item.OrderKind, item.Amount, accountId, item.Name);
                var recordId = await metadataService.CreateDynamicRecordAsync(item.Document.Id, data);
                createdRecords.Add((item.Document, recordId, item.Number, item.PostingDocumentType));

                await metadataService.PostDocumentAsync(item.Document.Id, recordId);
                var posting = await LoadPostingAsync(context, item.Number, item.PostingDocumentType, cancellationToken);

                AssertEqual(item.ExpectedDebit, posting.Debit, $"{item.Name}: дебет");
                AssertEqual(item.ExpectedCredit, posting.Credit, $"{item.Name}: кредит");
                AssertEqualDecimal(item.Amount, posting.Amount, $"{item.Name}: сумма");

                Report(progress, $"OK: {item.Name} -> Дт {posting.Debit} / Кт {posting.Credit}, {posting.Amount:N2}");
            }
        }
        finally
        {
            await CleanupRecordsAsync(context, createdRecords, cancellationToken);
            await CleanupAccountsAsync(context, testContext.ChartOfAccounts, testContext.CreatedAccountIds, cancellationToken);
        }
    }

    private static Dictionary<string, object> BuildDocumentData(
        MetadataObject document,
        string number,
        string orderKind,
        decimal amount,
        Guid correspondentAccountId,
        string description)
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

    private static async Task CleanupRecordsAsync(
        AppDbContext context,
        IReadOnlyCollection<(MetadataObject Document, Guid RecordId, string Number, string PostingDocumentType)> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
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
            foreach (var record in records)
            {
                await using (var deletePostings = connection.CreateCommand())
                {
                    deletePostings.CommandText = "DELETE FROM doc_postings WHERE doc_number = @number AND document_type = @type;";
                    deletePostings.Parameters.AddWithValue("@number", record.Number);
                    deletePostings.Parameters.AddWithValue("@type", record.PostingDocumentType);
                    await deletePostings.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var deleteDocument = connection.CreateCommand())
                {
                    deleteDocument.CommandText = $@"DELETE FROM {SqlNames.QuoteIdentifier(record.Document.TableName)} WHERE ""Id"" = @id;";
                    deleteDocument.Parameters.AddWithValue("@id", record.RecordId);
                    await deleteDocument.ExecuteNonQueryAsync(cancellationToken);
                }
            }
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
        deleted += await CleanupLegacyInitialPostingArtifactsAsync(context, cancellationToken);
        return deleted;
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
            Dictionary<string, Guid> accountIds)
        {
            CashOrderDocument = cashOrderDocument;
            ChartOfAccounts = chartOfAccounts;
            CreatedAccountIds = createdAccountIds;
            AccountIds = accountIds;
        }

        public MetadataObject CashOrderDocument { get; }
        public MetadataObject ChartOfAccounts { get; }
        public IReadOnlyCollection<Guid> CreatedAccountIds { get; }
        public Dictionary<string, Guid> AccountIds { get; }

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

            await new MetadataService(context).EnsureCashOrderPostingAccountsAsync();

            var (accounts, createdAccountIds) = await EnsureAccountIdsAsync(context, chart, TestAccountCodes, cancellationToken);
            if (createdAccountIds.Count > 0)
            {
                Report(progress,
                    $"INFO: в базе '{candidateName}' временно созданы счета для smoke-теста: {string.Join(", ", createdAccountIds.Select(id => id.ToString()[..8]))}.");
            }

            return new CashOrderTestContext(cashOrder, chart, createdAccountIds, accounts);
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
                "description" => "Временный счет для smoke-теста проводок кассовых ордеров",
                "level" => 1,
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

        private static string GetAccountName(string code)
        {
            return code switch
            {
                "3010" => "Касса",
                "4010" => "Расчеты с поставщиками",
                "6010" => "Доходы от реализации",
                "6810" => "Расчеты с персоналом по оплате труда",
                "6850" => "Расчеты с подотчетными лицами",
                _ => $"Счет {code}"
            };
        }

        private static string GetAccountType(string code)
        {
            return code switch
            {
                "3010" or "6850" => "Active",
                _ => "Passive"
            };
        }
    }
}











