using System.Globalization;
using System.Text.Json;
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
        var testContext = await CashOrderTestContext.TryCreateAsync(context, candidate.Name);
        if (testContext == null)
        {
            continue;
        }

        tested = true;
        await RunCashOrderPostingTestsAsync(context, testContext);
        Console.WriteLine($"OK: тесты проводок кассовых ордеров выполнены в базе '{candidate.DatabaseName}'.");
        return 0;
    }
    catch (Exception ex)
    {
        errors.Add($"{candidate.DatabaseName}: {ex.Message}");
    }
}

if (!tested)
{
    Console.Error.WriteLine("Не найдена информационная база с документами ПКО/РКО и справочником План счетов.");
}
else
{
    Console.Error.WriteLine("Тесты проводок кассовых ордеров завершились с ошибкой:");
}

foreach (var error in errors)
{
    Console.Error.WriteLine($"- {error}");
}

return 1;

static async Task RunCashOrderPostingTestsAsync(AppDbContext context, CashOrderTestContext testContext)
{
    var metadataService = new MetadataService(context);
    var runPrefix = DateTime.UtcNow.ToString("MMddHHmmss", CultureInfo.InvariantCulture);
    var createdRecords = new List<(MetadataObject Document, Guid RecordId, string Number)>();

    var cases = new[]
    {
        new PostingCase(
            Name: "ПКО: поступление оплаты от покупателей",
            Document: testContext.ReceiptDocument,
            Number: $"91{runPrefix}01",
            Amount: 15000m,
            CorrespondentAccount: "6010",
            ExpectedDebit: "3010",
            ExpectedCredit: "6010"),
        new PostingCase(
            Name: "РКО: оплата поставщикам",
            Document: testContext.PaymentDocument,
            Number: $"91{runPrefix}02",
            Amount: 8000m,
            CorrespondentAccount: "4010",
            ExpectedDebit: "4010",
            ExpectedCredit: "3010"),
        new PostingCase(
            Name: "РКО: выплата заработной платы",
            Document: testContext.PaymentDocument,
            Number: $"91{runPrefix}03",
            Amount: 25000m,
            CorrespondentAccount: "6810",
            ExpectedDebit: "6810",
            ExpectedCredit: "3010"),
        new PostingCase(
            Name: "РКО: выдача подотчетному лицу",
            Document: testContext.PaymentDocument,
            Number: $"91{runPrefix}04",
            Amount: 5000m,
            CorrespondentAccount: "6850",
            ExpectedDebit: "6850",
            ExpectedCredit: "3010")
    };

    try
    {
        foreach (var item in cases)
        {
            var accountId = testContext.AccountIds[item.CorrespondentAccount];
            var data = BuildDocumentData(item.Document, item.Number, item.Amount, accountId, item.Name);
            var recordId = await metadataService.CreateDynamicRecordAsync(item.Document.Id, data);
            createdRecords.Add((item.Document, recordId, item.Number));

            await metadataService.PostDocumentAsync(item.Document.Id, recordId);
            var posting = await LoadPostingAsync(context, item.Number, item.Document.Name);

            AssertEqual(item.ExpectedDebit, posting.Debit, $"{item.Name}: дебет");
            AssertEqual(item.ExpectedCredit, posting.Credit, $"{item.Name}: кредит");
            AssertEqualDecimal(item.Amount, posting.Amount, $"{item.Name}: сумма");

            Console.WriteLine($"OK: {item.Name} -> Дт {posting.Debit} / Кт {posting.Credit}, {posting.Amount:N2}");
        }
    }
    finally
    {
        await CleanupAsync(context, createdRecords);
        await CleanupAccountsAsync(context, testContext.ChartOfAccounts, testContext.CreatedAccountIds);
    }
}

static Dictionary<string, object> BuildDocumentData(
    MetadataObject document,
    string number,
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
        else if (IsAmountField(field))
            value = amount;
        else if (IsDescriptionField(field))
            value = $"Автотест: {description}";
        else if (IsPostedField(field))
            value = false;
        else if (IsCorrespondentAccountField(field))
            value = correspondentAccountId.ToString();

        if (value != null)
        {
            data[field.Name] = value;
        }
        else if (field.IsRequired)
        {
            data[field.Name] = DefaultValueFor(field);
        }
    }

    data.TryAdd("Номер", number);
    data.TryAdd("Дата", DateTime.Today);
    data.TryAdd("Сумма", amount);
    data.TryAdd("Основание", $"Автотест: {description}");
    data.TryAdd("Проведён", false);
    data.TryAdd("Корр. счет", correspondentAccountId.ToString());

    return data;
}

static object DefaultValueFor(MetadataField field)
{
    if (IsNumberField(field))
        return DateTime.UtcNow.ToString("MMddHHmmss", CultureInfo.InvariantCulture);
    if (IsDateField(field))
        return DateTime.Today;
    if (IsAmountField(field))
        return 0m;
    if (IsPostedField(field))
        return false;
    if (IsCorrespondentAccountField(field) ||
        field.DbColumnName.Contains("account", StringComparison.OrdinalIgnoreCase) ||
        field.Name.Contains("счет", StringComparison.OrdinalIgnoreCase))
    {
        return string.Empty;
    }

    return field.FieldType switch
    {
        "DateTime" => DateTime.Today,
        "Bool" => false,
        "Int" => 0,
        "Decimal" => 0m,
        _ => $"Автотест {field.Name}"
    };
}

static bool IsNumberField(MetadataField field)
{
    return field.DbColumnName.Equals("doc_number", StringComparison.OrdinalIgnoreCase) ||
           field.DbColumnName.Equals("number", StringComparison.OrdinalIgnoreCase) ||
           field.Name.Contains("номер", StringComparison.OrdinalIgnoreCase);
}

static bool IsDateField(MetadataField field)
{
    return field.DbColumnName.Equals("doc_date", StringComparison.OrdinalIgnoreCase) ||
           field.DbColumnName.Equals("date", StringComparison.OrdinalIgnoreCase) ||
           field.Name.Contains("дата", StringComparison.OrdinalIgnoreCase);
}

static bool IsAmountField(MetadataField field)
{
    return field.DbColumnName.Equals("amount", StringComparison.OrdinalIgnoreCase) ||
           field.Name.Contains("сумма", StringComparison.OrdinalIgnoreCase);
}

static bool IsDescriptionField(MetadataField field)
{
    return field.DbColumnName.Equals("basis", StringComparison.OrdinalIgnoreCase) ||
           field.DbColumnName.Equals("description", StringComparison.OrdinalIgnoreCase) ||
           field.DbColumnName.Equals("purpose", StringComparison.OrdinalIgnoreCase) ||
           field.DbColumnName.Equals("note", StringComparison.OrdinalIgnoreCase) ||
           field.Name.Contains("основан", StringComparison.OrdinalIgnoreCase) ||
           field.Name.Contains("примеч", StringComparison.OrdinalIgnoreCase) ||
           field.Name.Contains("назнач", StringComparison.OrdinalIgnoreCase);
}

static bool IsPostedField(MetadataField field)
{
    return field.DbColumnName.Equals("is_posted", StringComparison.OrdinalIgnoreCase) ||
           field.Name.Contains("провед", StringComparison.OrdinalIgnoreCase);
}

static bool IsCorrespondentAccountField(MetadataField field)
{
    return field.DbColumnName.Equals("correspondent_account", StringComparison.OrdinalIgnoreCase) ||
           field.Name.Contains("корр", StringComparison.OrdinalIgnoreCase);
}

static async Task<PostingResult> LoadPostingAsync(AppDbContext context, string number, string documentType)
{
    var connection = (NpgsqlConnection)context.Database.GetDbConnection();
    var opened = false;

    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
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

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Проводка для документа {documentType} N {number} не найдена.");
        }

        return new PostingResult(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2));
    }
    finally
    {
        if (opened)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task CleanupAsync(AppDbContext context, IReadOnlyCollection<(MetadataObject Document, Guid RecordId, string Number)> records)
{
    if (records.Count == 0)
    {
        return;
    }

    var connection = (NpgsqlConnection)context.Database.GetDbConnection();
    var opened = false;

    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
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
                deletePostings.Parameters.AddWithValue("@type", record.Document.Name);
                await deletePostings.ExecuteNonQueryAsync();
            }

            await using (var deleteDocument = connection.CreateCommand())
            {
                deleteDocument.CommandText = $@"DELETE FROM {SqlNames.QuoteIdentifier(record.Document.TableName)} WHERE ""Id"" = @id;";
                deleteDocument.Parameters.AddWithValue("@id", record.RecordId);
                await deleteDocument.ExecuteNonQueryAsync();
            }
        }
    }
    finally
    {
        if (opened)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task CleanupAccountsAsync(AppDbContext context, MetadataObject chartOfAccounts, IReadOnlyCollection<Guid> accountIds)
{
    if (accountIds.Count == 0)
    {
        return;
    }

    var connection = (NpgsqlConnection)context.Database.GetDbConnection();
    var opened = false;

    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
        opened = true;
    }

    try
    {
        foreach (var accountId in accountIds)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"DELETE FROM {SqlNames.QuoteIdentifier(chartOfAccounts.TableName)} WHERE ""Id"" = @id;";
            command.Parameters.AddWithValue("@id", accountId);
            await command.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        if (opened)
        {
            await connection.CloseAsync();
        }
    }
}

static void AssertEqual(string expected, string actual, string subject)
{
    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{subject}: ожидалось '{expected}', получено '{actual}'.");
    }
}

static void AssertEqualDecimal(decimal expected, decimal actual, string subject)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{subject}: ожидалось {expected}, получено {actual}.");
    }
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
        {
            continue;
        }

        var settings = JsonSerializer.Deserialize<TestSettings>(File.ReadAllText(path));
        if (settings != null)
        {
            return settings;
        }
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
            {
                result.Add(new(name, databaseName, connectionString));
            }
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

internal sealed record PostingCase(
    string Name,
    MetadataObject Document,
    string Number,
    decimal Amount,
    string CorrespondentAccount,
    string ExpectedDebit,
    string ExpectedCredit);

internal sealed record PostingResult(string Debit, string Credit, decimal Amount);

internal sealed class CashOrderTestContext
{
    private CashOrderTestContext(
        MetadataObject receiptDocument,
        MetadataObject paymentDocument,
        MetadataObject chartOfAccounts,
        IReadOnlyCollection<Guid> createdAccountIds,
        Dictionary<string, Guid> accountIds)
    {
        ReceiptDocument = receiptDocument;
        PaymentDocument = paymentDocument;
        ChartOfAccounts = chartOfAccounts;
        CreatedAccountIds = createdAccountIds;
        AccountIds = accountIds;
    }

    public MetadataObject ReceiptDocument { get; }
    public MetadataObject PaymentDocument { get; }
    public MetadataObject ChartOfAccounts { get; }
    public IReadOnlyCollection<Guid> CreatedAccountIds { get; }
    public Dictionary<string, Guid> AccountIds { get; }

    public static async Task<CashOrderTestContext?> TryCreateAsync(AppDbContext context, string candidateName)
    {
        if (!await HasRequiredTableAsync(context, "MetadataObjects"))
        {
            return null;
        }

        var receipt = await context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Приходный кассовый ордер");
        var payment = await context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Расходный кассовый ордер");
        var chart = await context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name.StartsWith("План счетов"));

        if (receipt == null || payment == null || chart == null)
        {
            return null;
        }

        await new MetadataService(context).EnsureCashOrderPostingAccountsAsync();
        await new TestPostingMetadataSeedService(context).EnsureAsync(createTestPostings: false);

        var (accounts, createdAccountIds) = await EnsureAccountIdsAsync(
            context,
            chart,
            new[] { "3010", "4010", "6010", "6810", "6850" });

        if (createdAccountIds.Count > 0)
        {
            Console.WriteLine(
                $"INFO: в базе '{candidateName}' временно созданы счета для smoke-теста: {string.Join(", ", createdAccountIds.Select(id => id.ToString()[..8]))}.");
        }

        return new CashOrderTestContext(receipt, payment, chart, createdAccountIds, accounts);
    }

    private static async Task<bool> HasRequiredTableAsync(AppDbContext context, string tableName)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != System.Data.ConnectionState.Open)
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
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<Dictionary<string, Guid>> LoadAccountIdsAsync(
        AppDbContext context,
        string tableName,
        IReadOnlyCollection<string> accountCodes)
    {
        var result = new Dictionary<string, Guid>();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"SELECT ""Id"", ""code"" FROM {SqlNames.QuoteIdentifier(tableName)} WHERE ""code"" = ANY(@codes);";
            command.Parameters.AddWithValue("@codes", accountCodes.ToArray());

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result[reader.GetString(1)] = reader.GetGuid(0);
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    private static async Task<(Dictionary<string, Guid> Accounts, List<Guid> CreatedIds)> EnsureAccountIdsAsync(
        AppDbContext context,
        MetadataObject chart,
        IReadOnlyCollection<string> accountCodes)
    {
        var accounts = await LoadAccountIdsAsync(context, chart.TableName, accountCodes);
        var createdIds = new List<Guid>();

        foreach (var code in accountCodes.Where(code => !accounts.ContainsKey(code)))
        {
            var accountId = Guid.NewGuid();
            await InsertAccountAsync(context, chart, accountId, code);
            accounts[code] = accountId;
            createdIds.Add(accountId);
        }

        return (accounts, createdIds);
    }

    private static async Task InsertAccountAsync(AppDbContext context, MetadataObject chart, Guid accountId, string code)
    {
        var columns = new List<string> { "\"Id\"", "\"CreatedAt\"", "\"UpdatedAt\"" };
        var values = new List<string> { "@id", "NOW()", "NOW()" };
        var parameters = new List<NpgsqlParameter> { new("@id", accountId) };

        foreach (var field in chart.Fields.OrderBy(item => item.Order))
        {
            if (string.IsNullOrWhiteSpace(field.DbColumnName))
            {
                continue;
            }

            columns.Add(SqlNames.QuoteIdentifier(field.DbColumnName));
            var parameterName = $"@{field.DbColumnName}";
            values.Add(parameterName);
            parameters.Add(new NpgsqlParameter(parameterName, BuildAccountFieldValue(field, code)));
        }

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
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
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
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

internal static class SqlNames
{
    public static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
