using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Services;

public sealed class TestPostingMetadataSeedService
{
    private readonly AppDbContext _context;
    private readonly MetadataService _metadataService;

    public TestPostingMetadataSeedService(AppDbContext context)
    {
        _context = context;
        _metadataService = new MetadataService(context);
    }

    public async Task EnsureAsync(bool createTestPostings)
    {
        var scenarios = await EnsureScenarioCatalogAsync();
        await EnsureScenarioRowsAsync(scenarios);

        if (createTestPostings)
            await EnsureCashOrderTestPostingsAsync();
    }

    private async Task<MetadataObject> EnsureScenarioCatalogAsync()
    {
        const string name = "Тестовые сценарии проводок";
        var existing = await _context.MetadataObjects.Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == name);
        if (existing != null)
        {
            await _metadataService.CreateDynamicTableAsync(existing);
            return existing;
        }

        var configId = await _context.MetadataConfigurations.Select(item => (Guid?)item.Id).FirstOrDefaultAsync();
        var catalog = new MetadataObject
        {
            Name = name,
            TableName = "catalog_test_posting_scenarios",
            ObjectType = "Catalog",
            Description = "Контрольные сценарии проверки бухгалтерских проводок",
            Icon = "🧪",
            Order = await NextOrderAsync("Catalog"),
            IsSystem = true,
            MetadataConfigId = configId,
            Fields = new List<MetadataField>
            {
                Field("Код", "code", "String", 1, true, length: 20),
                Field("Наименование", "name", "String", 2, true, length: 200),
                Field("Документ", "document_name", "String", 3, true, length: 120),
                Field("Сумма", "amount", "Decimal", 4, true),
                Field("Корр. счет", "correspondent_account", "String", 5, true, length: 20),
                Field("Ожидаемый дебет", "expected_debit", "String", 6, true, length: 20),
                Field("Ожидаемый кредит", "expected_credit", "String", 7, true, length: 20),
                Field("Создавать проводку", "create_posting", "Bool", 8, true),
                Field("Описание", "description", "String", 9, false, length: 500),
                Field("Активен", "is_active", "Bool", 10, true)
            }
        };

        foreach (var field in catalog.Fields)
            field.MetadataObjectId = catalog.Id;

        await _context.MetadataObjects.AddAsync(catalog);
        await _context.SaveChangesAsync();
        await _metadataService.CreateDynamicTableAsync(catalog);
        return catalog;
    }

    private async Task EnsureScenarioRowsAsync(MetadataObject catalog)
    {
        var scenarios = new[]
        {
            new Scenario("KO-001", "ПКО: поступление оплаты от покупателей", "Приходный кассовый ордер", 15000m, "6010", "3010", "6010"),
            new Scenario("KO-002", "РКО: оплата поставщикам", "Расходный кассовый ордер", 8000m, "4010", "4010", "3010"),
            new Scenario("KO-003", "РКО: выплата заработной платы", "Расходный кассовый ордер", 25000m, "6810", "6810", "3010"),
            new Scenario("KO-004", "РКО: выдача подотчетному лицу", "Расходный кассовый ордер", 5000m, "6850", "6850", "3010")
        };

        foreach (var scenario in scenarios)
        {
            var sql = $@"
                INSERT INTO ""{catalog.TableName}""
                (""Id"", ""code"", ""name"", ""document_name"", ""amount"", ""correspondent_account"",
                 ""expected_debit"", ""expected_credit"", ""create_posting"", ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"")
                SELECT
                    '{Guid.NewGuid()}',
                    @code,
                    @name,
                    @documentName,
                    @amount,
                    @correspondentAccount,
                    @expectedDebit,
                    @expectedCredit,
                    true,
                    @description,
                    true,
                    NOW(),
                    NOW()
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""{catalog.TableName}"" WHERE ""code"" = @code
                );";

            await _context.Database.ExecuteSqlRawAsync(sql,
                new NpgsqlParameter("@code", scenario.Code),
                new NpgsqlParameter("@name", scenario.Name),
                new NpgsqlParameter("@documentName", scenario.DocumentName),
                new NpgsqlParameter("@amount", scenario.Amount),
                new NpgsqlParameter("@correspondentAccount", scenario.CorrespondentAccount),
                new NpgsqlParameter("@expectedDebit", scenario.ExpectedDebit),
                new NpgsqlParameter("@expectedCredit", scenario.ExpectedCredit),
                new NpgsqlParameter("@description", "Сценарий автопроверки проводок кассовых ордеров"));
        }
    }

    private async Task EnsureCashOrderTestPostingsAsync()
    {
        var scenarios = await LoadScenariosAsync();
        if (scenarios.Count == 0)
            return;

        var receipt = await _context.MetadataObjects.Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Приходный кассовый ордер");
        var payment = await _context.MetadataObjects.Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Расходный кассовый ордер");
        var chart = await _context.MetadataObjects
            .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "План счетов");

        if (receipt == null || payment == null || chart == null)
            return;

        var accountIds = await LoadAccountIdsAsync(chart.TableName);
        var index = 1;
        foreach (var scenario in scenarios.Where(item => item.CreatePosting))
        {
            var document = scenario.DocumentName == "Приходный кассовый ордер" ? receipt : payment;
            if (!accountIds.TryGetValue(scenario.CorrespondentAccount, out var accountId))
                continue;

            var number = $"88{index:0000}";
            if (await PostingExistsAsync(number, document.Name))
            {
                index++;
                continue;
            }

            var data = new Dictionary<string, object>
            {
                ["Номер"] = number,
                ["Дата"] = DateTime.Today,
                ["Сумма"] = scenario.Amount,
                ["Основание"] = scenario.Name,
                ["Примечание"] = "Тестовая проводка при создании инфобазы",
                ["Проведён"] = false,
                ["Корр. счет"] = accountId.ToString()
            };

            var recordId = await _metadataService.CreateDynamicRecordAsync(document.Id, data);
            await _metadataService.PostDocumentAsync(document.Id, recordId);
            index++;
        }
    }

    private async Task<List<Scenario>> LoadScenariosAsync()
    {
        var catalog = await _context.MetadataObjects
            .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Тестовые сценарии проводок");
        if (catalog == null)
            return new List<Scenario>();

        var result = new List<Scenario>();
        var connection = _context.Database.GetDbConnection();
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await _context.Database.OpenConnectionAsync();
            opened = true;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT ""code"", ""name"", ""document_name"", ""amount"", ""correspondent_account"",
                       ""expected_debit"", ""expected_credit"", ""create_posting""
                FROM ""{catalog.TableName}""
                WHERE COALESCE(""is_active"", true) = true
                ORDER BY ""code"";";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new Scenario(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDecimal(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetBoolean(7)));
            }
        }
        finally
        {
            if (opened)
                await _context.Database.CloseConnectionAsync();
        }

        return result;
    }

    private async Task<Dictionary<string, Guid>> LoadAccountIdsAsync(string chartTableName)
    {
        var result = new Dictionary<string, Guid>();
        var connection = _context.Database.GetDbConnection();
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await _context.Database.OpenConnectionAsync();
            opened = true;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"SELECT ""Id"", ""code"" FROM ""{chartTableName}"" WHERE ""code"" IN ('3010','4010','6010','6810','6850');";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result[reader.GetString(1)] = reader.GetGuid(0);
        }
        finally
        {
            if (opened)
                await _context.Database.CloseConnectionAsync();
        }

        return result;
    }

    private async Task<bool> PostingExistsAsync(string number, string documentType)
    {
        var connection = _context.Database.GetDbConnection();
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await _context.Database.OpenConnectionAsync();
            opened = true;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT EXISTS (
                    SELECT 1
                    FROM doc_postings
                    WHERE doc_number = @number AND document_type = @documentType
                );";
            var numberParameter = command.CreateParameter();
            numberParameter.ParameterName = "@number";
            numberParameter.Value = number;
            command.Parameters.Add(numberParameter);

            var typeParameter = command.CreateParameter();
            typeParameter.ParameterName = "@documentType";
            typeParameter.Value = documentType;
            command.Parameters.Add(typeParameter);

            return Convert.ToBoolean(await command.ExecuteScalarAsync());
        }
        finally
        {
            if (opened)
                await _context.Database.CloseConnectionAsync();
        }
    }

    private async Task<int> NextOrderAsync(string objectType)
    {
        return (await _context.MetadataObjects
            .Where(item => item.ObjectType == objectType)
            .MaxAsync(item => (int?)item.Order) ?? 0) + 1;
    }

    private static MetadataField Field(
        string name,
        string dbColumnName,
        string fieldType,
        int order,
        bool required,
        int length = 0)
    {
        return new MetadataField
        {
            Name = name,
            DbColumnName = dbColumnName,
            FieldType = fieldType,
            Order = order,
            IsRequired = required,
            Length = length
        };
    }

    private sealed record Scenario(
        string Code,
        string Name,
        string DocumentName,
        decimal Amount,
        string CorrespondentAccount,
        string ExpectedDebit,
        string ExpectedCredit,
        bool CreatePosting = true);
}
