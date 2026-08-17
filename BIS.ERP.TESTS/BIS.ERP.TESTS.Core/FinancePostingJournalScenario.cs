using System.Reflection;
using BIS.ERP.Data;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public sealed class FinancePostingJournalScenario : SmokeTestScenarioBase
{
    private const string TestDocumentPrefix = "TEST-FIN-JOURNAL-";
    private static readonly DateTime TestPostingDate = new(2026, 7, 14);

    private static readonly PostingDocumentExpectation[] PostingDocuments =
    [
        new("Выписка счет-фактур", "Выписка счет-фактур"),
        new("Регистрация счет-фактур", "Регистрация счет-фактур"),
        new("Приходный кассовый ордер", "Расходный/Приходный КО"),
        new("Расходный кассовый ордер", "Расходный/Приходный КО"),
        new("Исходящее платежное поручение", "Платежное поручение"),
        new("Входящее платежное поручение", "Платежное поручение"),
        new("Авансовый отчет", "Авансовый отчет"),
        new("Платежная ведомость", "Платежная ведомость"),
        new("Расчет курсовой разницы", "Расчет курсовой разницы")
    ];

    public override string Code => "finance-posting-journal";
    public override string Name => "Финансы: центральный журнал проводок";
    public override string Category => "Финансы";
    public override string Description =>
        "Проверяет общий журнал doc_postings, типы финансовых проводок и маршрут открытия исходных документов.";

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
                    details.Add($"{candidate.DatabaseName}: удалено контрольных проводок: {deleted}.");
                    continue;
                }

                await VerifyMetadataAsync(context, candidate.DatabaseName, details, errors, cancellationToken);

                if (options.Command == SmokeTestCommand.Run)
                {
                    await CleanupControlDataAsync(context, cancellationToken);
                    await InsertControlPostingsAsync(context, cancellationToken);
                    details.Add($"{candidate.DatabaseName}: создан контрольный набор проводок: {PostingDocuments.Length}.");
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

        if (controlRows.Count == 0)
        {
            if (command == SmokeTestCommand.Run)
                errors.Add($"{databaseName}: контрольные проводки не попали в журнал.");
            else
                details.Add($"{databaseName}: контрольный набор отсутствует. Для строгой проверки выполните finance-posting-journal run.");
            return;
        }

        foreach (var expectation in PostingDocuments)
        {
            var row = controlRows.FirstOrDefault(item =>
                item.DocumentType.Equals(expectation.PostingDocumentType, StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                errors.Add($"{databaseName}: в журнале нет контрольной проводки '{expectation.PostingDocumentType}'.");
                continue;
            }

            if (!row.ModuleName.Equals("Финансы", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{databaseName}: проводка '{expectation.PostingDocumentType}' показана в модуле '{row.ModuleName}', ожидался модуль 'Финансы'.");
        }

        details.Add($"{databaseName}: общий журнал вернул контрольных проводок: {controlRows.Count}.");
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

    private static async Task InsertControlPostingsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        for (var index = 0; index < PostingDocuments.Length; index++)
        {
            var expectation = PostingDocuments[index];
            await context.Database.ExecuteSqlRawAsync(@"
                INSERT INTO doc_postings
                    (""Id"", posting_date, doc_number, document_type, module_code,
                     debit_account, credit_account, amount_kgs, amount_currency,
                     description, is_active, ""CreatedAt"", ""UpdatedAt"")
                VALUES
                    (@id, @date, @number, @type, @moduleName,
                     @debit, @credit, @amount, 0,
                     @description, true, NOW(), NOW());",
                [
                    new NpgsqlParameter("@id", Guid.NewGuid()),
                    new NpgsqlParameter("@date", TestPostingDate),
                    new NpgsqlParameter("@number", $"{TestDocumentPrefix}{index + 1:00}"),
                    new NpgsqlParameter("@type", expectation.PostingDocumentType),
                    new NpgsqlParameter("@moduleName", "Финансы"),
                    new NpgsqlParameter("@debit", "14100000"),
                    new NpgsqlParameter("@credit", "61100000"),
                    new NpgsqlParameter("@amount", 100m + index),
                    new NpgsqlParameter("@description", $"Контрольная проводка: {expectation.PostingDocumentType}")
                ],
                cancellationToken);
        }
    }

    private static async Task<int> CleanupControlDataAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        return await context.Database.ExecuteSqlRawAsync(@"
            DELETE FROM doc_postings
            WHERE doc_number LIKE @prefix;",
            [new NpgsqlParameter("@prefix", $"{TestDocumentPrefix}%")],
            cancellationToken);
    }

    private sealed record PostingDocumentExpectation(
        string PostingDocumentType,
        string SourceDocumentName);
}



