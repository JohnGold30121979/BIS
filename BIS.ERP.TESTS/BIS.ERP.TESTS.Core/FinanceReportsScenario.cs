using BIS.ERP.Data;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;

namespace BIS.ERP.Testing;

public sealed class FinanceReportsScenario : SmokeTestScenarioBase
{
    private static readonly (string ReportCode, string LineCode)[] RequiredReportLines =
    {
        ("Balance", "100"),
        ("Balance", "110"),
        ("Balance", "120"),
        ("Balance", "200"),
        ("Balance", "210"),
        ("Balance", "220"),
        ("Balance", "300"),
        ("ProfitLoss", "100"),
        ("ProfitLoss", "110"),
        ("ProfitLoss", "190"),
        ("ProfitLoss", "200"),
        ("ProfitLoss", "210"),
        ("ProfitLoss", "220"),
        ("ProfitLoss", "290")
    };

    private static readonly (string ReportCode, string LineCode)[] RequiredLinesWithAccounts =
    {
        ("Balance", "110"),
        ("Balance", "120"),
        ("Balance", "210"),
        ("Balance", "220"),
        ("Balance", "300"),
        ("ProfitLoss", "100"),
        ("ProfitLoss", "110"),
        ("ProfitLoss", "200"),
        ("ProfitLoss", "210"),
        ("ProfitLoss", "220")
    };

    public override string Code => "finance-reports";
    public override string Name => "Финансы: отчеты";
    public override string Category => "Финансы";
    public override string Description => "Проверяет основу Fox-совместимых финансовых отчетов: формульные строки баланса и финансовых результатов.";
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (options.Command == SmokeTestCommand.Cleanup)
        {
            return SmokeTestResult.Failure(
                "Сценарий не создает тестовые данные и не требует удаления. Используйте verify или run.");
        }

        var details = new List<string>();
        var errors = new List<string>();

        VerifyFoxReportSources(details, errors, progress);
        VerifyImplementation(details, errors);
        await VerifyDatabaseSchemaAsync(details, errors, progress, cancellationToken);

        return errors.Count == 0
            ? SmokeTestResult.Success("Проверка финансовых отчетов завершена успешно.", details.ToArray())
            : SmokeTestResult.Failure("Проверка финансовых отчетов выявила ошибки.", errors.Concat(details));
    }

    private static void VerifyFoxReportSources(
        List<string> details,
        List<string> errors,
        IProgress<string>? progress)
    {
        var foxRoot = ResolveFoxReportRoot();
        if (string.IsNullOrWhiteSpace(foxRoot))
        {
            details.Add("Fox-источники главного модуля не найдены: проверка исходных формул пропущена.");
            Report(progress, details[^1]);
            return;
        }

        var balanceProgram = Path.Combine(foxRoot, "d1_bal.prg");
        var financialResultsProgram = Path.Combine(foxRoot, "d2_ot_2011.prg");
        var collectionProgram = Path.Combine(foxRoot, "b1_f6.prg");

        if (!File.Exists(balanceProgram))
            errors.Add($"Не найден Fox-файл баланса: {balanceProgram}.");
        if (!File.Exists(financialResultsProgram))
            errors.Add($"Не найден Fox-файл финансовых результатов: {financialResultsProgram}.");
        if (!File.Exists(collectionProgram))
            errors.Add($"Не найден Fox-файл сбора финансовых результатов: {collectionProgram}.");

        if (File.Exists(balanceProgram))
        {
            var source = File.ReadAllText(balanceProgram);
            if (!source.Contains("form_st", StringComparison.OrdinalIgnoreCase) ||
                !source.Contains("SUMMAN", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Fox-баланс не содержит ожидаемую логику формульных строк form_st/SUMMAN.");
            }
        }

        if (File.Exists(financialResultsProgram))
        {
            var source = File.ReadAllText(financialResultsProgram);
            if (!source.Contains("formula", StringComparison.OrdinalIgnoreCase))
                errors.Add("Fox-финрезультаты не содержат ожидаемую обработку formula.");
        }

        details.Add($"Fox-источники отчетов проверены: {foxRoot}.");
        Report(progress, details[^1]);
    }

    private static void VerifyImplementation(List<string> details, List<string> errors)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(TestEnvironment.ResolveTestsRootDirectory(), ".."));
        var modelPath = Path.Combine(projectRoot, "Models", "AccountingInfrastructure.cs");
        var balanceModelPath = Path.Combine(projectRoot, "Models", "BalanceModels.cs");
        var servicePath = Path.Combine(projectRoot, "Services", "BalanceService.cs");
        var periodServicePath = Path.Combine(projectRoot, "Services", "AccountingPeriodService.cs");
        var setupViewPath = Path.Combine(projectRoot, "Views", "AccountingSetupView.xaml");
        var setupCodePath = Path.Combine(projectRoot, "Views", "AccountingSetupView.xaml.cs");

        RequireFile(modelPath, errors);
        RequireFile(balanceModelPath, errors);
        RequireFile(servicePath, errors);
        RequireFile(periodServicePath, errors);
        RequireFile(setupViewPath, errors);
        RequireFile(setupCodePath, errors);

        var modelSource = ReadFile(modelPath);
        var balanceModelSource = ReadFile(balanceModelPath);
        var serviceSource = ReadFile(servicePath);
        var periodServiceSource = ReadFile(periodServicePath);
        var setupViewSource = ReadFile(setupViewPath);
        var setupCodeSource = ReadFile(setupCodePath);

        RequireSource(modelSource, "public string Formula", "В модели строки отчетности нет поля Formula.", errors);
        RequireSource(modelSource, "public decimal FixedAmount", "В модели строки отчетности нет поля FixedAmount.", errors);
        RequireSource(periodServiceSource, "ADD COLUMN IF NOT EXISTS \"\"Formula\"\"", "Схема БД не добавляет колонку Formula безопасно.", errors);
        RequireSource(periodServiceSource, "ADD COLUMN IF NOT EXISTS \"\"FixedAmount\"\"", "Схема БД не добавляет колонку FixedAmount безопасно.", errors);
        RequireSource(serviceSource, "EvaluateReportFormula", "В BalanceService не найден расчетчик формул строк отчетности.", errors);
        RequireSource(serviceSource, "CalculateBalanceLineAmounts", "В BalanceService не найден расчет строк баланса с формулами.", errors);
        RequireSource(serviceSource, "CalculateProfitLossLineAmounts", "В BalanceService не найден расчет строк финрезультатов с формулами.", errors);
        RequireSource(serviceSource, "ResolveConfiguredSectionTotal", "В BalanceService не найден выбор итоговой формульной строки.", errors);
        RequireSource(balanceModelSource, "TotalIncomeOverride", "Финансовые результаты не поддерживают итог из формульной строки.", errors);
        RequireSource(setupViewSource, "Header=\"Формула\"", "В окне настройки отчетности нет колонки Формула.", errors);
        RequireSource(setupViewSource, "Header=\"Фикс. сумма\"", "В окне настройки отчетности нет колонки фиксированной суммы.", errors);
        RequireSource(setupCodeSource, "entity.Formula", "Окно настройки отчетности не сохраняет формулу.", errors);
        RequireSource(setupCodeSource, "entity.FixedAmount", "Окно настройки отчетности не сохраняет фиксированную сумму.", errors);

        details.Add("Реализация формульных строк отчетности проверена по коду.");
    }

    private static async Task VerifyDatabaseSchemaAsync(
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
                await new AccountingPeriodService(context).EnsureSchemaAsync();

                checkedAnyDatabase = true;
                var missingColumns = await FindMissingFinancialReportColumnsAsync(context, cancellationToken);
                if (missingColumns.Count > 0)
                    errors.Add($"{candidate.DatabaseName}: в FinancialReportLines нет колонок {string.Join(", ", missingColumns)}.");
                else
                    details.Add($"{candidate.DatabaseName}: схема FinancialReportLines поддерживает формулы отчетности.");

                var missingReportLines = await FindMissingPresetReportLinesAsync(context, cancellationToken);
                if (missingReportLines.Count > 0)
                    errors.Add($"{candidate.DatabaseName}: не заполнены строки отчетности {string.Join(", ", missingReportLines)}.");
                else
                    details.Add($"{candidate.DatabaseName}: базовые строки баланса и финрезультатов заполнены.");

                var missingAccountLinks = await FindMissingPresetAccountLinksAsync(context, cancellationToken);
                if (missingAccountLinks.Count > 0)
                    errors.Add($"{candidate.DatabaseName}: строки отчетности без привязки к счетам {string.Join(", ", missingAccountLinks)}.");
                else
                    details.Add($"{candidate.DatabaseName}: строки отчетности привязаны к классам счетов.");
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate.DatabaseName}: {ex.Message}");
            }
        }

        if (!checkedAnyDatabase)
            errors.Add("Не найдена ни одна база, где можно проверить схему FinancialReportLines.");
    }

    private static async Task<List<string>> FindMissingFinancialReportColumnsAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var requiredColumns = new[] { "Formula", "FixedAmount" };
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'FinancialReportLines';";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                existingColumns.Add(reader.GetString(0));
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }

        return requiredColumns
            .Where(column => !existingColumns.Contains(column))
            .ToList();
    }

    private static async Task<List<string>> FindMissingPresetReportLinesAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var existingLines = await context.FinancialReportLines.AsNoTracking()
            .Where(item => item.ReportCode == "Balance" || item.ReportCode == "ProfitLoss")
            .Select(item => item.ReportCode + ":" + item.LineCode)
            .ToListAsync(cancellationToken);
        var existing = existingLines.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return RequiredReportLines
            .Select(item => item.ReportCode + ":" + item.LineCode)
            .Where(item => !existing.Contains(item))
            .ToList();
    }

    private static async Task<List<string>> FindMissingPresetAccountLinksAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var requiredKeys = RequiredLinesWithAccounts
            .Select(item => item.ReportCode + ":" + item.LineCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requiredLines = await context.FinancialReportLines.AsNoTracking()
            .Where(item =>
                (item.ReportCode == "Balance" || item.ReportCode == "ProfitLoss") &&
                requiredKeys.Contains(item.ReportCode + ":" + item.LineCode))
            .Select(item => new { item.Id, item.ReportCode, item.LineCode })
            .ToListAsync(cancellationToken);

        var missing = new List<string>();
        foreach (var requiredLine in RequiredLinesWithAccounts)
        {
            var key = requiredLine.ReportCode + ":" + requiredLine.LineCode;
            var line = requiredLines.FirstOrDefault(item =>
                string.Equals(item.ReportCode, requiredLine.ReportCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.LineCode, requiredLine.LineCode, StringComparison.OrdinalIgnoreCase));
            if (line == null)
            {
                missing.Add(key);
                continue;
            }

            var hasAccountLinks = await context.FinancialReportLineAccounts.AsNoTracking()
                .AnyAsync(item => item.LineId == line.Id, cancellationToken);
            if (!hasAccountLinks)
                missing.Add(key);
        }

        return missing;
    }

    private static string? ResolveFoxReportRoot()
    {
        var explicitPath = Environment.GetEnvironmentVariable("BIS_ERP_FOX_GLAV_DIR");
        if (!string.IsNullOrWhiteSpace(explicitPath) && Directory.Exists(explicitPath))
            return explicitPath;

        var candidates = new[]
        {
            @"E:\Virt_shar_dir\Комтек софт\fox\glav_vpf9_ecf",
            Path.Combine(TestEnvironment.ResolveTestsRootDirectory(), "..", "..", "Комтек софт", "fox", "glav_vpf9_ecf")
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(Directory.Exists);
    }

    private static void RequireFile(string path, List<string> errors)
    {
        if (!File.Exists(path))
            errors.Add($"Не найден файл реализации: {path}.");
    }

    private static string ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private static void RequireSource(string source, string marker, string error, List<string> errors)
    {
        if (!source.Contains(marker, StringComparison.Ordinal))
            errors.Add(error);
    }
}
