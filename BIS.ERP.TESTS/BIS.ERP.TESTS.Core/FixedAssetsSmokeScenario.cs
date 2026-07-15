using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public sealed class FixedAssetsSmokeScenario : SmokeTestScenarioBase
{
    private const string TestDocumentPrefix = "TEST-ASSET-";
    private const string TestAssetCode = "TEST-ASSET-0001";
    private const string TestAssetInventoryNumber = "TEST-ASSET-0001";
    private const string AutoDepreciationDocumentPrefix = "DEP-204206-TESTASSET";

    private static readonly DateTime PeriodStart = new(2042, 6, 1);
    private static readonly DateTime PeriodEnd = new(2042, 6, 30);

    private static readonly ControlAccount[] ControlAccounts =
    [
        new("24109991", "Контрольный счет учета ОС", "Активный"),
        new("24209991", "Контрольный счет амортизации ОС", "Пассивный"),
        new("33909991", "Контрольный счет расчетов по ОС", "Пассивный"),
        new("54209991", "Контрольный счет переоценки ОС", "Пассивный"),
        new("74709991", "Контрольный счет расходов по ОС", "Активный")
    ];

    public override string Code => "fixed-assets-smoke";
    public override string Name => "Основные средства: smoke-тест оборотки";
    public override string Category => "Основные средства";
    public override string Description =>
        "Создает ОС, проводит покупку, ввод, амортизацию, переоценку, передачу, частичное выбытие, закрывает период и сверяет оборотку ОС.";

    public override async Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var details = new List<string>();
        var errors = new List<string>();
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
                await EnsureEnvironmentAsync(context, cancellationToken);

                if (options.Command == SmokeTestCommand.Cleanup)
                {
                    var deleted = await CleanupControlDataAsync(context, cancellationToken);
                    details.Add($"{candidate.DatabaseName}: удалено контрольных записей: {deleted}.");
                    continue;
                }

                if (options.Command == SmokeTestCommand.Run)
                {
                    await CleanupControlDataAsync(context, cancellationToken);
                    await EnsureEnvironmentAsync(context, cancellationToken);
                    await CreateAndPostControlDocumentsAsync(context, progress, cancellationToken);
                    await CollectAndClosePeriodAsync(context, candidate.DatabaseName, progress, cancellationToken);
                }

                await VerifyTurnoverSnapshotAsync(context, candidate.DatabaseName, details, errors, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate.DatabaseName}: {ex.Message}");
            }
        }

        if (!checkedAnyDatabase)
            errors.Add("Не найдена информационная база BIS ERP для smoke-теста основных средств.");

        return errors.Count == 0
            ? SmokeTestResult.Success("Smoke-тест основных средств завершен успешно.", details.ToArray())
            : SmokeTestResult.Failure("Smoke-тест основных средств выявил ошибки.", errors.Concat(details));
    }

    private static async Task EnsureEnvironmentAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        await new RuntimeSchemaFixService(context).EnsureAsync();
        await new MetadataService(context).InitializePredefinedCatalogsAsync(Guid.Empty);
        await new ModuleMetadataService(context).EnsureDefaultModulesAsync();
        await new DocumentationMetadataSeedService(context).EnsureAsync();
        await new AccountingPeriodService(context).EnsureSchemaAsync();
        _ = await new MetadataService(context).GetAllMetadataObjectsAsync();
    }

    private static async Task CreateAndPostControlDocumentsAsync(
        AppDbContext context,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var metadataService = new MetadataService(context);
        var assetCatalog = await LoadMetadataObjectAsync(context, "Catalog", "Основные средства", cancellationToken);

        var accountIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in ControlAccounts)
            accountIds[account.Code] = await EnsureAccountAsync(context, account, cancellationToken);

        Report(progress, "Создание контрольной карточки ОС.");
        var assetId = await metadataService.CreateDynamicRecordAsync(assetCatalog.Id, new Dictionary<string, object>
        {
            ["Код"] = TestAssetCode,
            ["Инвентарный номер"] = TestAssetInventoryNumber,
            ["Наименование"] = "Контрольный объект ОС для smoke-теста",
            ["Первоначальная стоимость"] = 0m,
            ["Ликвидационная стоимость"] = 0m,
            ["Накопленная амортизация"] = 0m,
            ["Остаточная стоимость"] = 0m,
            ["Срок полезного использования, мес."] = 0,
            ["Норма амортизации, %"] = 12m,
            ["Месячная амортизация"] = 0m,
            ["Амортизация по пробегу"] = false,
            ["Счет учета"] = accountIds["24109991"],
            ["Счет амортизации"] = accountIds["24209991"],
            ["Затратный счет"] = accountIds["74709991"],
            ["Класс ОС"] = 1,
            ["Активен"] = true,
            ["Описание"] = "Создано smoke-тестом ОС"
        });

        await CreateAndPostAsync(context, metadataService, "Покупка ОС", new()
        {
            ["Номер"] = TestDocumentPrefix + "PUR-001",
            ["Дата"] = PeriodStart.AddDays(1),
            ["Основное средство"] = assetId,
            ["Сумма"] = 10000m,
            ["Счет дебета"] = accountIds["24109991"],
            ["Счет кредита"] = accountIds["33909991"],
            ["Дата приобретения"] = PeriodStart.AddDays(1),
            ["Дата начала амортизации"] = PeriodStart,
            ["Норма амортизации, %"] = 12m,
            ["Счет амортизации"] = accountIds["24209991"],
            ["Затратный счет"] = accountIds["74709991"],
            ["Ликвидационная стоимость"] = 0m,
            ["Класс ОС"] = 1,
            ["Основание"] = "Smoke-тест ОС: покупка"
        }, cancellationToken);

        await CreateAndPostAsync(context, metadataService, "Ввод ОС в эксплуатацию", new()
        {
            ["Номер"] = TestDocumentPrefix + "COM-001",
            ["Дата"] = PeriodStart.AddDays(2),
            ["Основное средство"] = assetId,
            ["Сумма"] = 10000m,
            ["Счет дебета"] = accountIds["24109991"],
            ["Счет кредита"] = accountIds["33909991"],
            ["Дата ввода в эксплуатацию"] = PeriodStart.AddDays(2),
            ["Дата начала амортизации"] = PeriodStart,
            ["Норма амортизации, %"] = 12m,
            ["Счет амортизации"] = accountIds["24209991"],
            ["Затратный счет"] = accountIds["74709991"],
            ["Ликвидационная стоимость"] = 0m,
            ["Класс ОС"] = 1,
            ["Основание"] = "Smoke-тест ОС: ввод"
        }, cancellationToken);

        await CreateAndPostAsync(context, metadataService, "Начисление амортизации", new()
        {
            ["Номер"] = TestDocumentPrefix + "DEP-MAN-001",
            ["Дата"] = PeriodStart.AddDays(9),
            ["Основное средство"] = assetId,
            ["Сумма"] = 100m,
            ["Сумма амортизации"] = 100m,
            ["Счет дебета"] = accountIds["74709991"],
            ["Счет кредита"] = accountIds["24209991"],
            ["Счет амортизации"] = accountIds["24209991"],
            ["Затратный счет"] = accountIds["74709991"],
            ["Основание"] = "Smoke-тест ОС: ручная амортизация"
        }, cancellationToken);

        await CreateAndPostAsync(context, metadataService, "Переоценка ОС", new()
        {
            ["Номер"] = TestDocumentPrefix + "REV-001",
            ["Дата"] = PeriodStart.AddDays(14),
            ["Основное средство"] = assetId,
            ["Сумма"] = 0m,
            ["Сумма изменения стоимости"] = 500m,
            ["Сумма изменения амортизации"] = 20m,
            ["Счет дебета"] = accountIds["24109991"],
            ["Счет переоценки"] = accountIds["54209991"],
            ["Основание"] = "Smoke-тест ОС: переоценка"
        }, cancellationToken);

        await CreateAndPostAsync(context, metadataService, "Передача ОС в подотчет", new()
        {
            ["Номер"] = TestDocumentPrefix + "TRN-001",
            ["Дата"] = PeriodStart.AddDays(17),
            ["Основное средство"] = assetId,
            ["Сумма"] = 10500m,
            ["Основание"] = "Smoke-тест ОС: передача"
        }, cancellationToken);

        await CreateAndPostAsync(context, metadataService, "Частичная реализация ОС", new()
        {
            ["Номер"] = TestDocumentPrefix + "DSP-001",
            ["Дата"] = PeriodStart.AddDays(19),
            ["Основное средство"] = assetId,
            ["Сумма"] = 1050m,
            ["Счет дебета"] = accountIds["74709991"],
            ["Счет кредита"] = accountIds["24109991"],
            ["Счет списания остаточной стоимости"] = accountIds["74709991"],
            ["Сумма реализации"] = 0m,
            ["Основание"] = "Smoke-тест ОС: частичное выбытие"
        }, cancellationToken);
    }

    private static async Task CreateAndPostAsync(
        AppDbContext context,
        MetadataService metadataService,
        string documentName,
        Dictionary<string, object> values,
        CancellationToken cancellationToken)
    {
        var document = await LoadMetadataObjectAsync(context, "Document", documentName, cancellationToken);
        var recordId = await metadataService.CreateDynamicRecordAsync(document.Id, values);
        await metadataService.PostDocumentAsync(document.Id, recordId);
    }

    private static async Task CollectAndClosePeriodAsync(
        AppDbContext context,
        string databaseName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, "Сбор и закрытие контрольного периода ОС.");
        var service = new AccountingPeriodService(context);
        var period = await service.CollectAsync(PeriodStart, PeriodEnd);
        var statuses = await service.GetModuleStatusesAsync(period.Id);
        var financeModule = statuses.FirstOrDefault(item =>
            item.ModuleCode.Equals(ModuleMetadataService.FinanceCode, StringComparison.OrdinalIgnoreCase) ||
            item.ModuleName.Equals("Финансы", StringComparison.OrdinalIgnoreCase));

        foreach (var module in statuses
            .Where(item => financeModule == null || item.ModuleId != financeModule.ModuleId)
            .OrderBy(item => item.CloseOrder)
            .ThenBy(item => item.ModuleName))
        {
            await service.CloseModuleAsync(period.Id, module.ModuleId);
        }

        if (financeModule != null)
            await service.CloseModuleAsync(period.Id, financeModule.ModuleId);

        await service.CloseAsync(period.Id);
        Report(progress, $"{databaseName}: контрольный период ОС закрыт.");
    }

    private static async Task VerifyTurnoverSnapshotAsync(
        AppDbContext context,
        string databaseName,
        List<string> details,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var period = await new AccountingPeriodService(context).FindAsync(PeriodStart, PeriodEnd);
        if (period == null)
        {
            details.Add($"{databaseName}: контрольный период ОС отсутствует. Для строгой сверки выполните fixed-assets-smoke run.");
            return;
        }

        if (!period.IsLocked || !period.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase))
            errors.Add($"{databaseName}: контрольный период ОС найден, но не закрыт итоговым балансом.");

        var snapshot = await context.FixedAssetPeriodBalances.AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.PeriodId == period.Id &&
                item.InventoryNumber == TestAssetInventoryNumber,
                cancellationToken);

        if (snapshot == null)
        {
            errors.Add($"{databaseName}: не найден снимок оборотки ОС по инвентарному номеру {TestAssetInventoryNumber}.");
            return;
        }

        CompareAmount(errors, databaseName, "Стоимость на начало", 0m, snapshot.OpeningCost);
        CompareAmount(errors, databaseName, "Поступление", 10000m, snapshot.AcquisitionCost);
        CompareAmount(errors, databaseName, "Переоценка стоимости", 500m, snapshot.RevaluationCost);
        CompareAmount(errors, databaseName, "Внутреннее поступление", 10500m, snapshot.TransferInCost);
        CompareAmount(errors, databaseName, "Внутреннее выбытие", 10500m, snapshot.TransferOutCost);
        CompareAmount(errors, databaseName, "Выбытие", 1050m, snapshot.DisposalCost);
        CompareAmount(errors, databaseName, "Ручная амортизация", 100m, snapshot.ManualDepreciation);
        CompareAmount(errors, databaseName, "Автоматическая амортизация", 94.50m, snapshot.AutomaticDepreciation);
        CompareAmount(errors, databaseName, "Корректировка амортизации", 20m, snapshot.DepreciationAdjustment);
        CompareAmount(errors, databaseName, "Амортизация выбывших ОС", 12m, snapshot.DisposalDepreciation);
        CompareAmount(errors, databaseName, "Стоимость на конец", 9450m, snapshot.ClosingCost);
        CompareAmount(errors, databaseName, "Амортизация на конец", 202.50m, snapshot.ClosingDepreciation);
        CompareAmount(errors, databaseName, "Остаточная стоимость на конец", 9247.50m, snapshot.ClosingCarryingAmount);

        var postedDocuments = await CountPostedControlDocumentsAsync(context, cancellationToken);
        if (postedDocuments < 7)
            errors.Add($"{databaseName}: ожидалось минимум 7 проведенных документов ОС с учетом автоматической амортизации, найдено {postedDocuments}.");

        details.Add($"{databaseName}: оборотка ОС сошлась по контрольному сценарию.");
    }

    private static async Task<int> CleanupControlDataAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var periodIds = await context.AccountingPeriods.AsNoTracking()
            .Where(item =>
                item.StartDate == DateTime.SpecifyKind(PeriodStart, DateTimeKind.Utc) &&
                item.EndDate == DateTime.SpecifyKind(PeriodEnd, DateTimeKind.Utc))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        if (periodIds.Count > 0)
        {
            deleted += await context.AccountingPeriodModuleStates
                .Where(item => periodIds.Contains(item.PeriodId))
                .ExecuteDeleteAsync(cancellationToken);
            deleted += await context.AccountTurnoverSnapshots
                .Where(item => periodIds.Contains(item.PeriodId))
                .ExecuteDeleteAsync(cancellationToken);
            deleted += await context.FixedAssetPeriodBalances
                .Where(item => periodIds.Contains(item.PeriodId))
                .ExecuteDeleteAsync(cancellationToken);
            deleted += await context.AccountingPeriods
                .Where(item => periodIds.Contains(item.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var assetCatalog = await context.MetadataObjects.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Основные средства", cancellationToken);
        var assetIds = assetCatalog == null
            ? new List<Guid>()
            : await LoadIdsByColumnPrefixAsync(context, assetCatalog.TableName, "inventory_number", TestDocumentPrefix, cancellationToken);

        foreach (var documentName in ModuleMetadataService.FixedAssetDocumentNames)
        {
            var document = await context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == documentName, cancellationToken);
            if (document == null)
                continue;

            deleted += await DeleteDocumentRowsAsync(context, document.TableName, assetIds, cancellationToken);
        }

        deleted += await DeletePostingsAsync(context, cancellationToken);

        if (assetCatalog != null)
        {
            var deleteAssetsSql = $@"
                DELETE FROM {SqlNames.QuoteIdentifier(assetCatalog.TableName)}
                WHERE COALESCE(""inventory_number"", '') LIKE @prefix
                   OR COALESCE(""code"", '') LIKE @prefix;";
            deleted += await context.Database.ExecuteSqlRawAsync(deleteAssetsSql,
                new NpgsqlParameter("@prefix", TestDocumentPrefix + "%"));
        }

        var chart = await context.MetadataObjects.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "План счетов", cancellationToken);
        if (chart != null)
        {
            foreach (var account in ControlAccounts)
            {
                var deleteAccountSql = $@"
                    DELETE FROM {SqlNames.QuoteIdentifier(chart.TableName)}
                    WHERE ""code"" = @code;";
                deleted += await context.Database.ExecuteSqlRawAsync(deleteAccountSql,
                    new NpgsqlParameter("@code", account.Code));
            }
        }

        return deleted;
    }

    private static async Task<Guid> EnsureAccountAsync(
        AppDbContext context,
        ControlAccount account,
        CancellationToken cancellationToken)
    {
        var chart = await LoadMetadataObjectAsync(context, "Catalog", "План счетов", cancellationToken);
        var existing = await FindIdByTextColumnAsync(context, chart.TableName, "code", account.Code, cancellationToken);
        if (existing.HasValue)
            return existing.Value;

        var id = Guid.NewGuid();
        var insertAccountSql = $@"
            INSERT INTO {SqlNames.QuoteIdentifier(chart.TableName)}
                (""Id"", ""code"", ""name"", ""account_type"", ""description"", ""level"", ""is_active"", ""CreatedAt"", ""UpdatedAt"")
            VALUES
                (@id, @code, @name, @type, @description, 1, true, NOW(), NOW());";
        await context.Database.ExecuteSqlRawAsync(insertAccountSql,
            new NpgsqlParameter("@id", id),
            new NpgsqlParameter("@code", account.Code),
            new NpgsqlParameter("@name", account.Name),
            new NpgsqlParameter("@type", account.AccountType),
            new NpgsqlParameter("@description", "Создано smoke-тестом ОС"));
        return id;
    }

    private static async Task<MetadataObject> LoadMetadataObjectAsync(
        AppDbContext context,
        string objectType,
        string name,
        CancellationToken cancellationToken)
    {
        return await context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item =>
                item.ObjectType == objectType &&
                item.Name == name,
                cancellationToken)
            ?? throw new InvalidOperationException($"Объект метаданных «{name}» не найден.");
    }

    private static async Task<Guid?> FindIdByTextColumnAsync(
        AppDbContext context,
        string tableName,
        string columnName,
        string value,
        CancellationToken cancellationToken)
    {
        if (!await HasTableAsync(context, tableName, cancellationToken))
            return null;

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT ""Id""
                FROM {SqlNames.QuoteIdentifier(tableName)}
                WHERE ""{columnName}"" = @value
                LIMIT 1;";
            command.Parameters.AddWithValue("@value", value);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is Guid id ? id : result == null || result == DBNull.Value ? null : Guid.Parse(result.ToString()!);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task<List<Guid>> LoadIdsByColumnPrefixAsync(
        AppDbContext context,
        string tableName,
        string columnName,
        string prefix,
        CancellationToken cancellationToken)
    {
        var result = new List<Guid>();
        if (!await HasTableAsync(context, tableName, cancellationToken))
            return result;

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT ""Id""
                FROM {SqlNames.QuoteIdentifier(tableName)}
                WHERE COALESCE(""{columnName}"", '') LIKE @prefix;";
            command.Parameters.AddWithValue("@prefix", prefix + "%");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result.Add(reader.GetGuid(0));
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }

        return result;
    }

    private static async Task<int> DeleteDocumentRowsAsync(
        AppDbContext context,
        string tableName,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken)
    {
        if (!await HasTableAsync(context, tableName, cancellationToken))
            return 0;

        var assetClause = assetIds.Count == 0
            ? "false"
            : $@"""asset_id"" IN ({string.Join(", ", assetIds.Select(id => $"'{id}'"))})";

        var deleteDocumentsSql = $@"
            DELETE FROM {SqlNames.QuoteIdentifier(tableName)}
            WHERE COALESCE(""doc_number"", '') LIKE @prefix
               OR COALESCE(""doc_number"", '') LIKE @autoPrefix
               OR {assetClause};";
        return await context.Database.ExecuteSqlRawAsync(deleteDocumentsSql,
            new NpgsqlParameter("@prefix", TestDocumentPrefix + "%"),
            new NpgsqlParameter("@autoPrefix", AutoDepreciationDocumentPrefix + "%"));
    }

    private static async Task<int> DeletePostingsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (!await HasTableAsync(context, "doc_postings", cancellationToken))
            return 0;

        return await context.Database.ExecuteSqlRawAsync(@"
            DELETE FROM doc_postings
            WHERE COALESCE(doc_number, '') LIKE @prefix
               OR COALESCE(doc_number, '') LIKE @autoPrefix;",
            new NpgsqlParameter("@prefix", TestDocumentPrefix + "%"),
            new NpgsqlParameter("@autoPrefix", AutoDepreciationDocumentPrefix + "%"));
    }

    private static async Task<int> CountPostedControlDocumentsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var documentName in ModuleMetadataService.FixedAssetDocumentNames)
        {
            var document = await context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == documentName, cancellationToken);
            if (document == null || !await HasTableAsync(context, document.TableName, cancellationToken))
                continue;

            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var shouldClose = connection.State != System.Data.ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $@"
                    SELECT COUNT(*)
                    FROM {SqlNames.QuoteIdentifier(document.TableName)}
                    WHERE COALESCE(""is_posted"", false)
                      AND (COALESCE(""doc_number"", '') LIKE @prefix
                           OR COALESCE(""doc_number"", '') LIKE @autoPrefix);";
                command.Parameters.AddWithValue("@prefix", TestDocumentPrefix + "%");
                command.Parameters.AddWithValue("@autoPrefix", AutoDepreciationDocumentPrefix + "%");
                count += Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        return count;
    }

    private static async Task<bool> HasTableAsync(
        AppDbContext context,
        string tableName,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

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
            return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static void CompareAmount(
        List<string> errors,
        string databaseName,
        string fieldName,
        decimal expected,
        decimal actual)
    {
        if (Math.Abs(expected - actual) < 0.01m)
            return;

        errors.Add($"{databaseName}: ОС оборотка, {fieldName}: ожидалось {expected:N2}, получено {actual:N2}.");
    }

    private sealed record ControlAccount(string Code, string Name, string AccountType);
}
