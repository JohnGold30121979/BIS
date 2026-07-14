using BIS.ERP.Data;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public sealed class PaymentOrderClassificationScenario : SmokeTestScenarioBase
{
    public override string Code => "payment-order-classification";
    public override string Name => "Платежные поручения: классификация";
    public override string Category => "Финансы";
    public override string Description => "Проверяет справочник классификации платежей, связь с платежным поручением и импорт Fox DBF.";
    public override bool SupportsRun => false;
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (options.Command != SmokeTestCommand.Verify)
        {
            return SmokeTestResult.Failure(
                "Сценарий только проверяет настройку и не создает тестовые документы. Используйте режим verify.");
        }

        var details = new List<string>();
        var errors = new List<string>();

        VerifyFoxDbfAnalyzer(details, errors, progress);
        await VerifyMetadataAsync(details, errors, progress, cancellationToken);

        return errors.Count == 0
            ? SmokeTestResult.Success("Проверка классификации платежей завершена успешно.", details.ToArray())
            : SmokeTestResult.Failure("Проверка классификации платежей выявила ошибки.", errors.Concat(details));
    }

    private static void VerifyFoxDbfAnalyzer(
        List<string> details,
        List<string> errors,
        IProgress<string>? progress)
    {
        var filePath = ResolvePaymentClassificationDbfPath();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            details.Add("Fox DBF не найден: проверка импортера пропущена. Можно задать BIS_ERP_PAYMENT_CLASSIFICATION_DBF или BIS_ERP_FOX_DBF_DIR.");
            Report(progress, details[^1]);
            return;
        }

        try
        {
            var analysis = new PaymentClassificationDbfImportService().Analyze(filePath);
            if (analysis.LoadedItemsCount < 1000)
                errors.Add($"DBF классификации платежей разобран подозрительно коротко: {analysis.LoadedItemsCount} записей.");

            if (analysis.Items.All(item => item.Code != "10000000"))
                errors.Add("В DBF классификации платежей не найден контрольный код 10000000.");

            if (analysis.FieldMappings.All(item => item.SourceField != "KODPL") ||
                analysis.FieldMappings.All(item => item.SourceField != "NAME_PL"))
            {
                errors.Add("DBF-анализатор не содержит обязательные сопоставления KODPL/NAME_PL.");
            }

            details.Add($"Fox DBF проверен: {analysis.LoadedItemsCount} кодов, файл {filePath}.");
            Report(progress, details[^1]);
        }
        catch (Exception ex)
        {
            errors.Add($"Ошибка проверки DBF классификации платежей: {ex.Message}");
        }
    }

    private static string? ResolvePaymentClassificationDbfPath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("BIS_ERP_PAYMENT_CLASSIFICATION_DBF");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        var directoryPath = Environment.GetEnvironmentVariable("BIS_ERP_FOX_DBF_DIR");
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            var path = Path.Combine(directoryPath, "VID_PL.DBF");
            if (File.Exists(path))
                return path;
        }

        var candidates = new[]
        {
            @"E:\C#\BIS.ERP\FINN-лпск\FINN\VID_PL.DBF",
            Path.Combine(TestEnvironment.ResolveTestsRootDirectory(), "..", "..", "FINN-лпск", "FINN", "VID_PL.DBF")
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private static async Task VerifyMetadataAsync(
        List<string> details,
        List<string> errors,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var settings = LoadSettings();
        var candidates = await LoadDatabaseCandidatesAsync(settings, progress, cancellationToken);
        var checkedAnyDatabase = false;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, $"Проверяется база: {candidate.DatabaseName}");

            try
            {
                await using var context = new AppDbContext(candidate.ConnectionString);
                if (!await HasRequiredTableAsync(context, "MetadataObjects", cancellationToken))
                {
                    details.Add($"{candidate.DatabaseName}: таблица MetadataObjects не найдена, база пропущена.");
                    continue;
                }

                checkedAnyDatabase = true;
                await VerifyCandidateAsync(context, candidate.DatabaseName, details, errors, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate.DatabaseName}: {ex.Message}");
            }
        }

        if (!checkedAnyDatabase)
            errors.Add("Не найдена ни одна информационная база с метаданными BIS ERP.");
    }

    private static async Task VerifyCandidateAsync(
        AppDbContext context,
        string databaseName,
        List<string> details,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var catalog = await context.MetadataObjects
            .Include(item => item.Fields)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ObjectType == "Catalog" && item.Name == "Классификация платежей",
                cancellationToken);

        var document = await context.MetadataObjects
            .Include(item => item.Fields)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ObjectType == "Document" && item.Name == "Платежное поручение",
                cancellationToken);

        if (catalog == null)
        {
            errors.Add($"{databaseName}: не найден справочник 'Классификация платежей'.");
            return;
        }

        if (document == null)
        {
            errors.Add($"{databaseName}: не найден документ 'Платежное поручение'.");
            return;
        }

        VerifyCatalogFields(databaseName, catalog.Fields, errors);
        VerifyDocumentField(databaseName, document.Fields, errors);

        await VerifyTableColumnsAsync(
            context,
            databaseName,
            catalog.TableName,
            new[] { "code", "name", "external_code", "is_active", "description" },
            errors,
            cancellationToken);

        await VerifyTableColumnsAsync(
            context,
            databaseName,
            document.TableName,
            new[] { "payment_classification_id" },
            errors,
            cancellationToken);

        var loadedCount = await CountRowsAsync(context, catalog.TableName, cancellationToken);
        var invalidReferences = await CountInvalidPaymentClassificationReferencesAsync(
            context,
            document.TableName,
            catalog.TableName,
            cancellationToken);

        if (invalidReferences > 0)
            errors.Add($"{databaseName}: найдено битых ссылок на классификацию платежей: {invalidReferences}.");

        details.Add($"{databaseName}: метаданные проверены, загружено кодов классификации: {loadedCount}.");
    }

    private static void VerifyCatalogFields(
        string databaseName,
        IEnumerable<BIS.ERP.Models.MetadataField> fields,
        List<string> errors)
    {
        var byColumn = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
            .ToDictionary(field => field.DbColumnName!, StringComparer.OrdinalIgnoreCase);

        foreach (var column in new[] { "code", "name", "external_code", "is_active", "description" })
        {
            if (!byColumn.ContainsKey(column))
                errors.Add($"{databaseName}: в справочнике классификации платежей нет поля {column}.");
        }
    }

    private static void VerifyDocumentField(
        string databaseName,
        IEnumerable<BIS.ERP.Models.MetadataField> fields,
        List<string> errors)
    {
        var field = fields.FirstOrDefault(item =>
            item.DbColumnName?.Equals("payment_classification_id", StringComparison.OrdinalIgnoreCase) == true);

        if (field == null)
        {
            errors.Add($"{databaseName}: в платежном поручении нет поля payment_classification_id.");
            return;
        }

        if (!string.Equals(field.Name, "Классификация платежа", StringComparison.OrdinalIgnoreCase))
            errors.Add($"{databaseName}: поле payment_classification_id имеет неверное имя '{field.Name}'.");

        if (!string.Equals(field.FieldType, "Reference", StringComparison.OrdinalIgnoreCase))
            errors.Add($"{databaseName}: поле классификации платежа должно быть ссылкой.");

        if (!string.Equals(field.ReferenceCatalog, "Классификация платежей", StringComparison.OrdinalIgnoreCase))
            errors.Add($"{databaseName}: поле классификации платежа ссылается на '{field.ReferenceCatalog}', ожидался справочник классификации платежей.");
    }

    private static async Task VerifyTableColumnsAsync(
        AppDbContext context,
        string databaseName,
        string tableName,
        IReadOnlyCollection<string> expectedColumns,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var existingColumns = await LoadColumnNamesAsync(context, tableName, cancellationToken);
        if (existingColumns.Count == 0)
        {
            errors.Add($"{databaseName}: таблица {tableName} не найдена или не содержит колонок.");
            return;
        }

        foreach (var column in expectedColumns)
        {
            if (!existingColumns.Contains(column))
                errors.Add($"{databaseName}: в таблице {tableName} нет колонки {column}.");
        }
    }

    private static async Task<HashSet<string>> LoadColumnNamesAsync(
        AppDbContext context,
        string tableName,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @tableName;";
            command.Parameters.AddWithValue("@tableName", tableName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result.Add(reader.GetString(0));
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }

        return result;
    }

    private static async Task<int> CountRowsAsync(
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
            command.CommandText = $"SELECT COUNT(*) FROM {SqlNames.QuoteIdentifier(tableName)};";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task<int> CountInvalidPaymentClassificationReferencesAsync(
        AppDbContext context,
        string paymentOrderTableName,
        string classificationTableName,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT COUNT(*)
                FROM {SqlNames.QuoteIdentifier(paymentOrderTableName)} payment_order
                LEFT JOIN {SqlNames.QuoteIdentifier(classificationTableName)} classification
                    ON classification.""Id""::text = payment_order.""payment_classification_id""::text
                WHERE COALESCE(payment_order.""payment_classification_id""::text, '') <> ''
                  AND classification.""Id"" IS NULL;";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
}
