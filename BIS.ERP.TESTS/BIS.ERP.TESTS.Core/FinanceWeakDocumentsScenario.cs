using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public sealed class FinanceWeakDocumentsScenario : SmokeTestScenarioBase
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedDocumentColumns =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Авансовый отчет"] =
            [
                "employee_id",
                "advance_payment_id",
                "report_start_date",
                "report_end_date",
                "issue_document_number",
                "issue_document_date",
                "currency_id",
                "exchange_rate",
                "amount_currency",
                "accepted_amount",
                "overrun_amount",
                "return_amount"
            ],
            ["Доверенность"] =
            [
                "representative_id",
                "counterparty_id",
                "bank_account",
                "bank_name",
                "identity_document_name",
                "identity_document_number",
                "identity_document_date",
                "identity_document_issuer",
                "valid_until",
                "source_document_number",
                "source_document_date",
                "items_description",
                "quantity",
                "unit_name"
            ],
            ["Платежная ведомость"] =
            [
                "employee_id",
                "period_start_date",
                "period_end_date",
                "payment_account",
                "accrued_amount",
                "withheld_amount",
                "payable_amount",
                "currency_id",
                "exchange_rate",
                "amount_currency"
            ],
            ["Расчет курсовой разницы"] =
            [
                "calculation_date",
                "period_start_date",
                "period_end_date",
                "currency_id",
                "exchange_rate",
                "currency_account",
                "gain_account",
                "loss_account",
                "processed_balances",
                "created_postings",
                "gain_amount",
                "loss_amount"
            ]
        };

    private static readonly string[] ExpectedExchangeRateDifferenceCatalogColumns =
    [
        "account_id",
        "paired_account_id",
        "gain_account_id",
        "loss_account_id",
        "calculation_detail_mode",
        "currency_id",
        "module_code",
        "calculation_algorithm",
        "debit_report_line",
        "credit_report_line",
        "calculation_method",
        "is_active",
        "description"
    ];

    public override string Code => "finance-weak-documents";
    public override string Name => "Финансы: слабые документы";
    public override string Category => "Финансы";
    public override string Description => "Проверяет, что авансовый отчет, доверенность, платежная ведомость и расчет курсовой разницы не остались универсальными документами.";
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (options.Command == SmokeTestCommand.Cleanup)
        {
            return SmokeTestResult.Failure(
                "Сценарий не создает тестовые документы и не требует удаления данных. Используйте verify или run.");
        }

        var details = new List<string>();
        var errors = new List<string>();

        VerifyFoxSources(details, errors, progress);
        VerifySpecializedImplementation(details, errors);
        await VerifyMetadataAsync(options.Command, details, errors, progress, cancellationToken);

        return errors.Count == 0
            ? SmokeTestResult.Success("Проверка слабых финансовых документов завершена успешно.", details.ToArray())
            : SmokeTestResult.Failure("Проверка слабых финансовых документов выявила ошибки.", errors.Concat(details));
    }

    private static void VerifySpecializedImplementation(List<string> details, List<string> errors)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(TestEnvironment.ResolveTestsRootDirectory(), ".."));
        var requiredFiles = new[]
        {
            Path.Combine(projectRoot, "Views", "FinanceDocumentWorkView.xaml"),
            Path.Combine(projectRoot, "Views", "FinanceDocumentWorkView.xaml.cs"),
            Path.Combine(projectRoot, "Views", "Dialogs", "FinanceDocumentDialog.xaml"),
            Path.Combine(projectRoot, "Views", "Dialogs", "FinanceDocumentDialog.xaml.cs"),
            Path.Combine(projectRoot, "Services", "MetadataService.FinanceDocuments.cs")
        };

        foreach (var filePath in requiredFiles)
        {
            if (!File.Exists(filePath))
                errors.Add($"Не найден файл специализированной реализации: {filePath}.");
        }

        var implementationSources = requiredFiles
            .Where(File.Exists)
            .Select(File.ReadAllText)
            .ToList();

        var mainWorkWindowPath = Path.Combine(projectRoot, "Views", "MainWorkWindow.xaml.cs");
        if (!File.Exists(mainWorkWindowPath))
            return;

        var mainWorkWindowSource = File.ReadAllText(mainWorkWindowPath);
        if (!mainWorkWindowSource.Contains("\"FinanceDocument\"", StringComparison.Ordinal))
            errors.Add("В MainWorkWindow не найден специализированный тип навигации FinanceDocument.");
        if (!mainWorkWindowSource.Contains("FinanceDocumentWorkView", StringComparison.Ordinal))
            errors.Add("В MainWorkWindow не подключено окно FinanceDocumentWorkView.");

        var notReadyBlockStart = mainWorkWindowSource.IndexOf("NotReadyFinanceDocuments", StringComparison.Ordinal);
        if (notReadyBlockStart >= 0)
        {
            var notReadyBlockEnd = mainWorkWindowSource.IndexOf("};", notReadyBlockStart, StringComparison.Ordinal);
            var notReadyBlock = notReadyBlockEnd > notReadyBlockStart
                ? mainWorkWindowSource[notReadyBlockStart..notReadyBlockEnd]
                : string.Empty;
            foreach (var documentName in new[] { "Авансовый отчет", "Доверенность", "Платежная ведомость" })
            {
                if (notReadyBlock.Contains(documentName, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Документ '{documentName}' все еще скрыт в NotReadyFinanceDocuments.");
            }
        }

        var metadataServicePath = Path.Combine(projectRoot, "Services", "MetadataService.cs");
        var metadataServiceSource = File.Exists(metadataServicePath)
            ? File.ReadAllText(metadataServicePath)
            : string.Empty;
        foreach (var handlerName in new[] { "ProcessAdvanceReportAsync", "ProcessPowerOfAttorneyAsync", "ProcessPayrollStatementAsync" })
        {
            if (!metadataServiceSource.Contains(handlerName, StringComparison.Ordinal) &&
                !implementationSources.Any(source => source.Contains(handlerName, StringComparison.Ordinal)))
            {
                errors.Add($"Не найден обработчик проведения {handlerName}.");
            }
        }

        var exchangeRateDifferenceServicePath = Path.Combine(projectRoot, "Services", "ExchangeRateDifferenceService.cs");
        if (!File.Exists(exchangeRateDifferenceServicePath))
        {
            errors.Add("Не найден сервис расчета курсовой разницы.");
        }
        else
        {
            var exchangeRateDifferenceSource = File.ReadAllText(exchangeRateDifferenceServicePath);
            foreach (var expectedToken in new[]
                     {
                         "BuildMovementAwarePostingsAsync",
                         "calculation_detail_mode",
                         "calculation_method",
                         "DebitReportLine",
                         "CreditReportLine",
                         "PairedAccountCode",
                         "CalculationAlgorithm == 3"
                     })
            {
                if (!exchangeRateDifferenceSource.Contains(expectedToken, StringComparison.Ordinal))
                    errors.Add($"В сервисе курсовой разницы не найден признак Fox-совместимости: {expectedToken}.");
            }
        }

        details.Add("Специализированные формы и обработчики финансовых документов найдены в проекте.");
    }

    private static void VerifyFoxSources(
        List<string> details,
        List<string> errors,
        IProgress<string>? progress)
    {
        var directoryPath = ResolveFoxDbfDirectory();
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            details.Add("Fox DBF не найден: проверка источников пропущена. Можно задать BIS_ERP_FOX_DBF_DIR.");
            Report(progress, details[^1]);
            return;
        }

        var requiredSources = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["AVT_P.DBF"] = ["DEB", "CRED", "AVANS", "NAME", "PR_AKT", "PR_AKT2"],
            ["DOVER.DBF"] = ["DOK", "DATE", "RSCH1", "NAME_B", "FIO", "P_N", "P_SER", "P_KEM", "P_D", "KODSW"],
            ["DOVER_SW.DBF"] = ["KODSW", "N1", "E1", "K1", "PP1"],
            ["VED.DBF"] = ["DEBET", "KREDIT", "SUM", "SUM_V", "KOD_V", "KURS_V", "DOK", "VID_DOC"],
            ["KURS_R.DBF"] = ["DEB", "CRED", "SCH_S", "SCH_S1", "PR_RASH", "KOD_V", "KOD_ARM", "ALG", "D_STAT", "C_STAT"],
            ["TDOC3.DBF"] = ["VID_DOC", "NAME_DOC", "PROG", "PROG1", "PROG2", "SCH_NULL"]
        };

        foreach (var (fileName, requiredFields) in requiredSources)
        {
            var filePath = Path.Combine(directoryPath, fileName);
            if (!File.Exists(filePath))
            {
                errors.Add($"Fox-источник {fileName} не найден в {directoryPath}.");
                continue;
            }

            var header = ReadDbfHeader(filePath);
            foreach (var requiredField in requiredFields)
            {
                if (!header.FieldNames.Contains(requiredField))
                    errors.Add($"{fileName}: не найдено поле {requiredField}.");
            }

            details.Add($"{fileName}: записей {header.RecordCount}, полей {header.FieldNames.Count}.");
        }
    }

    private static async Task VerifyMetadataAsync(
        SmokeTestCommand command,
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
                if (command == SmokeTestCommand.Run)
                {
                    Report(progress, $"{candidate.DatabaseName}: применяются системные метаданные финансовых документов.");
                    var infoBaseId = await context.MetadataConfigurations
                        .Select(item => item.InfoBaseId)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (infoBaseId != Guid.Empty)
                        await new MetadataService(context).InitializePredefinedCatalogsAsync(infoBaseId);

                    await new MetadataService(context).EnsureFinanceCatalogStructuresAsync();
                    await new DocumentationMetadataSeedService(context).EnsureAsync();
                }

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
        foreach (var (documentName, expectedColumns) in ExpectedDocumentColumns)
        {
            var document = await context.MetadataObjects
                .Include(item => item.Fields)
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.ObjectType == "Document" && item.Name == documentName,
                    cancellationToken);

            if (document == null)
            {
                errors.Add($"{databaseName}: документ '{documentName}' не найден.");
                continue;
            }

            VerifyDocumentFields(databaseName, document, expectedColumns, errors);
            await VerifyTableColumnsAsync(
                context,
                databaseName,
                document.TableName,
                expectedColumns,
                errors,
                cancellationToken);
        }

        var exchangeRateDifferenceCatalog = await context.MetadataObjects
            .Include(item => item.Fields)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ObjectType == "Catalog" && item.Name == "Расчет курсовой разницы",
                cancellationToken);

        if (exchangeRateDifferenceCatalog == null)
        {
            errors.Add($"{databaseName}: справочник 'Расчет курсовой разницы' не найден.");
        }
        else
        {
            VerifyExchangeRateDifferenceCatalogFields(databaseName, exchangeRateDifferenceCatalog, errors);
            await VerifyTableColumnsAsync(
                context,
                databaseName,
                exchangeRateDifferenceCatalog.TableName,
                ExpectedExchangeRateDifferenceCatalogColumns,
                errors,
                cancellationToken);
        }

        details.Add($"{databaseName}: специализированные реквизиты финансовых документов проверены.");
    }

    private static void VerifyDocumentFields(
        string databaseName,
        MetadataObject document,
        IReadOnlyCollection<string> expectedColumns,
        List<string> errors)
    {
        var fieldsByColumn = document.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
            .ToDictionary(field => field.DbColumnName, StringComparer.OrdinalIgnoreCase);

        foreach (var column in expectedColumns)
        {
            if (!fieldsByColumn.ContainsKey(column))
                errors.Add($"{databaseName}: в документе '{document.Name}' нет поля {column}.");
        }

        if (document.Name == "Авансовый отчет" &&
            fieldsByColumn.TryGetValue("advance_payment_id", out var advancePaymentField) &&
            !string.Equals(advancePaymentField.ReferenceCatalog, "Авансовые платежи", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{databaseName}: поле advance_payment_id должно ссылаться на справочник 'Авансовые платежи'.");
        }

        if (document.Name == "Расчет курсовой разницы")
        {
            foreach (var accountColumn in new[] { "currency_account", "gain_account", "loss_account" })
            {
                if (fieldsByColumn.TryGetValue(accountColumn, out var accountField) &&
                    !string.Equals(accountField.ReferenceCatalog, "План счетов", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{databaseName}: поле {accountColumn} должно ссылаться на план счетов.");
                }
            }
        }
    }

    private static void VerifyExchangeRateDifferenceCatalogFields(
        string databaseName,
        MetadataObject catalog,
        List<string> errors)
    {
        var fieldsByColumn = catalog.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
            .ToDictionary(field => field.DbColumnName, StringComparer.OrdinalIgnoreCase);

        foreach (var column in ExpectedExchangeRateDifferenceCatalogColumns)
        {
            if (!fieldsByColumn.ContainsKey(column))
                errors.Add($"{databaseName}: в справочнике '{catalog.Name}' нет поля {column}.");
        }

        foreach (var accountColumn in new[] { "account_id", "paired_account_id", "gain_account_id", "loss_account_id" })
        {
            if (fieldsByColumn.TryGetValue(accountColumn, out var accountField) &&
                !string.Equals(accountField.ReferenceCatalog, "План счетов", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{databaseName}: поле {accountColumn} справочника курсовой разницы должно ссылаться на план счетов.");
            }
        }
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

    private static string? ResolveFoxDbfDirectory()
    {
        var explicitPath = Environment.GetEnvironmentVariable("BIS_ERP_FOX_DBF_DIR");
        if (!string.IsNullOrWhiteSpace(explicitPath) && Directory.Exists(explicitPath))
            return explicitPath;

        var candidates = new[]
        {
            @"E:\C#\BIS.ERP\FINN-лпск\FINN",
            Path.Combine(TestEnvironment.ResolveTestsRootDirectory(), "..", "..", "FINN-лпск", "FINN")
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(Directory.Exists);
    }

    private static DbfHeaderInfo ReadDbfHeader(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var recordCount = BitConverter.ToUInt32(bytes, 4);
        var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var offset = 32;
        while (offset + 32 <= bytes.Length && bytes[offset] != 0x0D)
        {
            var nameBytes = bytes.Skip(offset).Take(11).TakeWhile(item => item != 0).ToArray();
            var fieldName = System.Text.Encoding.ASCII.GetString(nameBytes).Trim();
            if (!string.IsNullOrWhiteSpace(fieldName))
                fieldNames.Add(fieldName);

            offset += 32;
        }

        return new DbfHeaderInfo(recordCount, fieldNames);
    }

    private sealed record DbfHeaderInfo(uint RecordCount, HashSet<string> FieldNames);
}
