using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public sealed class FinanceRegistersScenario : SmokeTestScenarioBase
{
    private const string TestDocumentPrefix = "TEST-FIN-REG-";
    private static readonly DateTime TestStartDate = new(2026, 1, 1);
    private static readonly DateTime TestEndDate = new(2026, 3, 31);
    private static readonly DateTime OpeningBalanceDate = new(2025, 12, 31);

    private static readonly string[] TestAccounts =
    [
        "19999001",
        "39999001",
        "59999001",
        "61199001",
        "81199001"
    ];

    private static readonly ControlBalanceExpectation[] ExpectedBalances =
    [
        new("19999001", 1000m, 0m, 250m, 70m, 1180m, 0m),
        new("39999001", 0m, 400m, 120m, 250m, 0m, 530m),
        new("59999001", 0m, 600m, 0m, 0m, 0m, 600m),
        new("61199001", 0m, 0m, 0m, 120m, 0m, 120m),
        new("81199001", 0m, 0m, 70m, 0m, 70m, 0m)
    ];

    private static readonly ControlPosting[] ControlPostings =
    [
        new(new DateTime(2026, 1, 10), "001", "19999001", "39999001", 250m, "Контрольный оборот января"),
        new(new DateTime(2026, 2, 5), "002", "81199001", "19999001", 70m, "Контрольный расход февраля"),
        new(new DateTime(2026, 3, 20), "003", "39999001", "61199001", 120m, "Контрольное закрытие марта")
    ];

    public override string Code => "finance-registers";
    public override string Name => "Финансы: ОСВ и главная книга";
    public override string Category => "Финансы";
    public override string Description =>
        "Создает контрольный набор проводок и строго сверяет ОСВ и главную книгу по суммам.";

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
                await new AccountingPeriodService(context).EnsureSchemaAsync();
                await EnsurePostingTableAsync(context, cancellationToken);

                if (options.Command == SmokeTestCommand.Cleanup)
                {
                    var deleted = await CleanupControlDataAsync(context, cancellationToken);
                    details.Add($"{candidate.DatabaseName}: удалено контрольных записей: {deleted}.");
                    continue;
                }

                if (options.Command == SmokeTestCommand.Run)
                {
                    await CleanupControlDataAsync(context, cancellationToken);
                    await InsertControlDataAsync(context, cancellationToken);
                    details.Add($"{candidate.DatabaseName}: контрольные остатки и проводки созданы.");
                }

                var hasControlData = await HasControlDataAsync(context, cancellationToken);
                if (!hasControlData)
                {
                    details.Add(
                        $"{candidate.DatabaseName}: контрольный набор отсутствует. Для строгой сверки выполните finance-registers run.");
                    continue;
                }

                await VerifyTrialBalanceAsync(context, candidate.DatabaseName, details, errors);
                await VerifyGeneralLedgerAsync(context, candidate.DatabaseName, details, errors);
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate.DatabaseName}: {ex.Message}");
            }
        }

        if (!checkedAnyDatabase)
            errors.Add("Не найдена информационная база BIS ERP для проверки ОСВ и главной книги.");

        return errors.Count == 0
            ? SmokeTestResult.Success("Проверка ОСВ и главной книги завершена успешно.", details.ToArray())
            : SmokeTestResult.Failure("Проверка ОСВ и главной книги выявила ошибки.", errors.Concat(details));
    }

    private static async Task EnsurePostingTableAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS doc_postings (
                ""Id"" uuid PRIMARY KEY,
                posting_date timestamp with time zone NOT NULL,
                doc_number varchar(80) NOT NULL,
                document_type varchar(120) NOT NULL,
                module_code varchar(50),
                debit_account varchar(50) NOT NULL,
                credit_account varchar(50) NOT NULL,
                amount_kgs numeric(18,2) NOT NULL,
                amount_currency numeric(18,2) NOT NULL DEFAULT 0,
                currency_id text,
                description text,
                is_active boolean NOT NULL DEFAULT true,
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
            );
            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS module_code varchar(50);
            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS amount_currency numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS currency_id text;
            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS description text;
            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;",
            cancellationToken);
    }

    private static async Task InsertControlDataAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var openingDate = DateTime.SpecifyKind(OpeningBalanceDate, DateTimeKind.Utc);
        await context.AccountOpeningBalances.AddRangeAsync(
            new AccountOpeningBalance
            {
                BalanceDate = openingDate,
                AccountCode = "19999001",
                Debit = 1000m,
                Credit = 0m
            },
            new AccountOpeningBalance
            {
                BalanceDate = openingDate,
                AccountCode = "39999001",
                Debit = 0m,
                Credit = 400m
            },
            new AccountOpeningBalance
            {
                BalanceDate = openingDate,
                AccountCode = "59999001",
                Debit = 0m,
                Credit = 600m
            });
        await context.SaveChangesAsync(cancellationToken);

        foreach (var posting in ControlPostings)
        {
            await context.Database.ExecuteSqlRawAsync(@"
                INSERT INTO doc_postings
                    (""Id"", posting_date, doc_number, document_type, module_code,
                     debit_account, credit_account, amount_kgs, amount_currency,
                     description, is_active, ""CreatedAt"", ""UpdatedAt"")
                VALUES
                    (@id, @date, @number, @type, @moduleCode,
                     @debit, @credit, @amount, 0,
                     @description, true, NOW(), NOW());",
                new NpgsqlParameter("@id", Guid.NewGuid()),
                new NpgsqlParameter("@date", DateTime.SpecifyKind(posting.Date, DateTimeKind.Utc)),
                new NpgsqlParameter("@number", TestDocumentPrefix + posting.NumberSuffix),
                new NpgsqlParameter("@type", "Контрольная проводка"),
                new NpgsqlParameter("@moduleCode", "Финансы"),
                new NpgsqlParameter("@debit", posting.DebitAccount),
                new NpgsqlParameter("@credit", posting.CreditAccount),
                new NpgsqlParameter("@amount", posting.Amount),
                new NpgsqlParameter("@description", posting.Description));
        }
    }

    private static async Task<int> CleanupControlDataAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var deletedPostings = await context.Database.ExecuteSqlRawAsync(@"
            DELETE FROM doc_postings
            WHERE doc_number LIKE @prefix;",
            new NpgsqlParameter("@prefix", TestDocumentPrefix + "%"));

        var openingDate = DateTime.SpecifyKind(OpeningBalanceDate, DateTimeKind.Utc);
        var deletedOpeningBalances = await context.AccountOpeningBalances
            .Where(balance => balance.BalanceDate == openingDate && TestAccounts.Contains(balance.AccountCode))
            .ExecuteDeleteAsync(cancellationToken);

        return deletedPostings + deletedOpeningBalances;
    }

    private static async Task<bool> HasControlDataAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var openingDate = DateTime.SpecifyKind(OpeningBalanceDate, DateTimeKind.Utc);
        var hasOpeningBalances = await context.AccountOpeningBalances.AsNoTracking()
            .AnyAsync(balance => balance.BalanceDate == openingDate && TestAccounts.Contains(balance.AccountCode), cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM doc_postings WHERE doc_number LIKE @prefix);";
            command.Parameters.AddWithValue("@prefix", TestDocumentPrefix + "%");
            var hasPostings = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
            return hasOpeningBalances && hasPostings;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task VerifyTrialBalanceAsync(
        AppDbContext context,
        string databaseName,
        List<string> details,
        List<string> errors)
    {
        var balances = await new BalanceService(context).GetTurnoverBalanceAsync(TestStartDate, TestEndDate);
        var balancesByAccount = balances
            .Where(balance => TestAccounts.Contains(balance.AccountCode))
            .ToDictionary(balance => balance.AccountCode, StringComparer.OrdinalIgnoreCase);

        foreach (var expected in ExpectedBalances)
        {
            if (!balancesByAccount.TryGetValue(expected.AccountCode, out var actual))
            {
                errors.Add($"{databaseName}: ОСВ не содержит контрольный счет {expected.AccountCode}.");
                continue;
            }

            CompareAmount(errors, databaseName, "ОСВ", expected.AccountCode, "Сальдо нач. Дт", expected.OpeningDebit, actual.OpeningDebit);
            CompareAmount(errors, databaseName, "ОСВ", expected.AccountCode, "Сальдо нач. Кт", expected.OpeningCredit, actual.OpeningCredit);
            CompareAmount(errors, databaseName, "ОСВ", expected.AccountCode, "Оборот Дт", expected.TurnoverDebit, actual.TurnoverDebit);
            CompareAmount(errors, databaseName, "ОСВ", expected.AccountCode, "Оборот Кт", expected.TurnoverCredit, actual.TurnoverCredit);
            CompareAmount(errors, databaseName, "ОСВ", expected.AccountCode, "Сальдо кон. Дт", expected.ClosingDebit, actual.ClosingDebit);
            CompareAmount(errors, databaseName, "ОСВ", expected.AccountCode, "Сальдо кон. Кт", expected.ClosingCredit, actual.ClosingCredit);
        }

        CompareAmount(errors, databaseName, "ОСВ", "ИТОГО", "Начальное сальдо Дт/Кт",
            ExpectedBalances.Sum(item => item.OpeningDebit),
            ExpectedBalances.Sum(item => item.OpeningCredit));
        CompareAmount(errors, databaseName, "ОСВ", "ИТОГО", "Обороты Дт/Кт",
            ExpectedBalances.Sum(item => item.TurnoverDebit),
            ExpectedBalances.Sum(item => item.TurnoverCredit));
        CompareAmount(errors, databaseName, "ОСВ", "ИТОГО", "Конечное сальдо Дт/Кт",
            ExpectedBalances.Sum(item => item.ClosingDebit),
            ExpectedBalances.Sum(item => item.ClosingCredit));

        details.Add($"{databaseName}: ОСВ по контрольному набору сошлась.");
    }

    private static async Task VerifyGeneralLedgerAsync(
        AppDbContext context,
        string databaseName,
        List<string> details,
        List<string> errors)
    {
        var ledger = await new BalanceService(context).GetGeneralLedgerAsync(TestStartDate.Year);
        var ledgerByAccount = ledger
            .Where(row => TestAccounts.Contains(row.AccountCode))
            .ToDictionary(row => row.AccountCode, StringComparer.OrdinalIgnoreCase);

        foreach (var expected in ExpectedBalances)
        {
            if (!ledgerByAccount.TryGetValue(expected.AccountCode, out var actual))
            {
                errors.Add($"{databaseName}: главная книга не содержит контрольный счет {expected.AccountCode}.");
                continue;
            }

            CompareAmount(errors, databaseName, "Главная книга", expected.AccountCode, "Сальдо нач. Дт", expected.OpeningDebit, actual.OpeningDebit);
            CompareAmount(errors, databaseName, "Главная книга", expected.AccountCode, "Сальдо нач. Кт", expected.OpeningCredit, actual.OpeningCredit);
            CompareAmount(errors, databaseName, "Главная книга", expected.AccountCode, "Год Дт", expected.TurnoverDebit, actual.YearTurnoverDebit);
            CompareAmount(errors, databaseName, "Главная книга", expected.AccountCode, "Год Кт", expected.TurnoverCredit, actual.YearTurnoverCredit);
            CompareAmount(errors, databaseName, "Главная книга", expected.AccountCode, "Сальдо кон. Дт", expected.ClosingDebit, actual.ClosingDebit);
            CompareAmount(errors, databaseName, "Главная книга", expected.AccountCode, "Сальдо кон. Кт", expected.ClosingCredit, actual.ClosingCredit);
        }

        AssertMonthTurnover(errors, databaseName, ledgerByAccount, "19999001", 1, debit: 250m, credit: 0m);
        AssertMonthTurnover(errors, databaseName, ledgerByAccount, "19999001", 2, debit: 0m, credit: 70m);
        AssertMonthTurnover(errors, databaseName, ledgerByAccount, "39999001", 1, debit: 0m, credit: 250m);
        AssertMonthTurnover(errors, databaseName, ledgerByAccount, "39999001", 3, debit: 120m, credit: 0m);
        AssertMonthTurnover(errors, databaseName, ledgerByAccount, "61199001", 3, debit: 0m, credit: 120m);
        AssertMonthTurnover(errors, databaseName, ledgerByAccount, "81199001", 2, debit: 70m, credit: 0m);

        details.Add($"{databaseName}: главная книга по контрольному набору сошлась.");
    }

    private static void AssertMonthTurnover(
        List<string> errors,
        string databaseName,
        IReadOnlyDictionary<string, GeneralLedger> ledgerByAccount,
        string accountCode,
        int month,
        decimal debit,
        decimal credit)
    {
        if (!ledgerByAccount.TryGetValue(accountCode, out var ledger))
            return;

        CompareAmount(errors, databaseName, "Главная книга", accountCode, $"{month:00} Дт", debit, ledger.MonthlyTurnoverDebit[month]);
        CompareAmount(errors, databaseName, "Главная книга", accountCode, $"{month:00} Кт", credit, ledger.MonthlyTurnoverCredit[month]);
    }

    private static void CompareAmount(
        List<string> errors,
        string databaseName,
        string reportName,
        string accountCode,
        string fieldName,
        decimal expected,
        decimal actual)
    {
        if (Math.Abs(expected - actual) < 0.01m)
            return;

        errors.Add(
            $"{databaseName}: {reportName}, {accountCode}, {fieldName}: ожидалось {expected:N2}, получено {actual:N2}.");
    }

    private sealed record ControlPosting(
        DateTime Date,
        string NumberSuffix,
        string DebitAccount,
        string CreditAccount,
        decimal Amount,
        string Description);

    private sealed record ControlBalanceExpectation(
        string AccountCode,
        decimal OpeningDebit,
        decimal OpeningCredit,
        decimal TurnoverDebit,
        decimal TurnoverCredit,
        decimal ClosingDebit,
        decimal ClosingCredit);
}
