using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public sealed class FinancePeriodClosingScenario : SmokeTestScenarioBase
{
    private const string TestDocumentPrefix = "TEST-FIN-CLOSE-";
    private const string ControlModuleCode = "PeriodClosingControl";
    private const string ControlModuleName = "Контрольный модуль закрытия";

    private static readonly DateTime PeriodStart = new(2035, 4, 1);
    private static readonly DateTime PeriodEnd = new(2035, 4, 30);
    private static readonly DateTime OpeningBalanceDate = new(2034, 12, 31);
    private static readonly DateTime PreviousControlOpeningBalanceDate = new(2035, 3, 31);

    private static readonly string[] ControlAccounts =
    [
        "19997001",
        "39997001",
        "59997001",
        "61197001",
        "81197001"
    ];

    public override string Code => "finance-period-closing";
    public override string Name => "Финансы: закрытие периода";
    public override string Category => "Финансы";
    public override string Description =>
        "Проверяет порядок закрытия модулей, финальное закрытие баланса, фиксацию оборотов и блокировку закрытого периода.";

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
                    await InsertControlDataAsync(context, cancellationToken);
                    await RunClosingWorkflowAsync(context, candidate.DatabaseName, details, errors, cancellationToken);
                    continue;
                }

                await VerifyClosedPeriodAsync(context, candidate.DatabaseName, details, errors, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate.DatabaseName}: {ex.Message}");
            }
        }

        if (!checkedAnyDatabase)
            errors.Add("Не найдена информационная база BIS ERP для проверки закрытия периода.");

        return errors.Count == 0
            ? SmokeTestResult.Success("Проверка закрытия периода завершена успешно.", details.ToArray())
            : SmokeTestResult.Failure("Проверка закрытия периода выявила ошибки.", errors.Concat(details));
    }

    private static async Task EnsureEnvironmentAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        await new ModuleMetadataService(context).EnsureDefaultModulesAsync();
        await new AccountingPeriodService(context).EnsureSchemaAsync();
        await EnsurePostingTableAsync(context, cancellationToken);
        await EnsureControlModuleAsync(context, cancellationToken);
    }

    private static async Task EnsureControlModuleAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var module = await context.MetadataModules
            .FirstOrDefaultAsync(item => item.Code == ControlModuleCode, cancellationToken);
        if (module == null)
        {
            await context.MetadataModules.AddAsync(new MetadataModule
            {
                Code = ControlModuleCode,
                Name = ControlModuleName,
                Description = "Тестовый модуль для проверки, что Финансы закрываются после всех остальных модулей.",
                Icon = string.Empty,
                Order = 990,
                CloseOrder = 950,
                IsActive = true,
                ParticipatesInPeriodClose = true,
                RequirePreviousModulesClosed = false,
                IsSystem = false
            }, cancellationToken);
        }
        else
        {
            module.Name = ControlModuleName;
            module.CloseOrder = 950;
            module.IsActive = true;
            module.ParticipatesInPeriodClose = true;
            module.RequirePreviousModulesClosed = false;
        }

        await context.SaveChangesAsync(cancellationToken);
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
                AccountCode = "19997001",
                Debit = 1000m,
                Credit = 0m
            },
            new AccountOpeningBalance
            {
                BalanceDate = openingDate,
                AccountCode = "39997001",
                Debit = 0m,
                Credit = 400m
            },
            new AccountOpeningBalance
            {
                BalanceDate = openingDate,
                AccountCode = "59997001",
                Debit = 0m,
                Credit = 600m
            });
        await context.SaveChangesAsync(cancellationToken);

        await InsertPostingAsync(context, new DateTime(2035, 4, 7), "001", "19997001", "39997001", 300m);
        await InsertPostingAsync(context, new DateTime(2035, 4, 15), "002", "81197001", "19997001", 80m);
        await InsertPostingAsync(context, new DateTime(2035, 4, 25), "003", "39997001", "61197001", 120m);
    }

    private static async Task InsertPostingAsync(
        AppDbContext context,
        DateTime date,
        string numberSuffix,
        string debitAccount,
        string creditAccount,
        decimal amount)
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
            new NpgsqlParameter("@date", DateTime.SpecifyKind(date, DateTimeKind.Utc)),
            new NpgsqlParameter("@number", TestDocumentPrefix + numberSuffix),
            new NpgsqlParameter("@type", "Контрольная проводка"),
            new NpgsqlParameter("@moduleCode", "Финансы"),
            new NpgsqlParameter("@debit", debitAccount),
            new NpgsqlParameter("@credit", creditAccount),
            new NpgsqlParameter("@amount", amount),
            new NpgsqlParameter("@description", "Контроль закрытия периода"));
    }

    private static async Task RunClosingWorkflowAsync(
        AppDbContext context,
        string databaseName,
        List<string> details,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var service = new AccountingPeriodService(context);
        var period = await service.CollectAsync(PeriodStart, PeriodEnd);
        details.Add($"{databaseName}: период собран, обороты и остатки зафиксированы.");

        await ExpectFailureAsync(
            () => service.CloseAsync(period.Id),
            "финальное закрытие до закрытия модулей должно быть запрещено",
            databaseName,
            errors);

        var statuses = await service.GetModuleStatusesAsync(period.Id);
        if (statuses.Any(item => item.ModuleCode.Equals(ModuleMetadataService.BalanceCode, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"{databaseName}: баланс попал в список рабочих модулей закрытия периода.");

        var financeModule = FindModule(statuses, ModuleMetadataService.FinanceCode, "Финансы");
        await ExpectFailureAsync(
            () => service.CloseModuleAsync(period.Id, financeModule.ModuleId),
            "Финансы не должны закрываться до остальных модулей",
            databaseName,
            errors);

        foreach (var module in statuses
            .Where(item => item.ParticipatesInPeriodClose && item.ModuleId != financeModule.ModuleId)
            .OrderBy(item => item.CloseOrder)
            .ThenBy(item => item.ModuleName))
        {
            await service.CloseModuleAsync(period.Id, module.ModuleId);
        }

        await service.CloseModuleAsync(period.Id, financeModule.ModuleId);
        await service.CloseAsync(period.Id);

        await VerifyClosedPeriodAsync(context, databaseName, details, errors, cancellationToken);
    }

    private static async Task VerifyClosedPeriodAsync(
        AppDbContext context,
        string databaseName,
        List<string> details,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var service = new AccountingPeriodService(context);
        var period = await service.FindAsync(PeriodStart, PeriodEnd);
        if (period == null)
        {
            details.Add($"{databaseName}: контрольный закрытый период отсутствует. Для проверки выполните finance-period-closing run.");
            return;
        }

        if (!period.IsLocked || !period.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase))
            errors.Add($"{databaseName}: контрольный период найден, но он не закрыт итоговым балансом.");

        var snapshots = await context.AccountTurnoverSnapshots.AsNoTracking()
            .Where(item => item.PeriodId == period.Id && ControlAccounts.Contains(item.AccountCode))
            .ToListAsync(cancellationToken);
        if (snapshots.Count == 0)
            errors.Add($"{databaseName}: не найдены зафиксированные снимки оборотов контрольного периода.");

        await CompareSnapshotsWithTrialBalanceAsync(context, databaseName, snapshots, errors);
        await CompareSnapshotsWithGeneralLedgerAsync(context, databaseName, snapshots, errors);

        await ExpectFailureAsync(
            () => service.EnsureDateCanBeModifiedAsync(new DateTime(2035, 4, 10)),
            "изменение документов закрытого периода должно быть запрещено",
            databaseName,
            errors);

        await ExpectFailureAsync(
            () => service.CollectAsync(PeriodStart, PeriodEnd),
            "пересбор закрытого периода должна быть запрещена",
            databaseName,
            errors);

        details.Add($"{databaseName}: закрытый период заблокирован, снимки оборотов сверены с ОСВ и главной книгой.");
    }

    private static async Task CompareSnapshotsWithTrialBalanceAsync(
        AppDbContext context,
        string databaseName,
        IReadOnlyCollection<AccountTurnoverSnapshot> snapshots,
        List<string> errors)
    {
        var balances = await new BalanceService(context).GetTurnoverBalanceAsync(PeriodStart, PeriodEnd);
        var balanceByAccount = balances
            .Where(item => ControlAccounts.Contains(item.AccountCode))
            .ToDictionary(item => item.AccountCode, StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in snapshots)
        {
            if (!balanceByAccount.TryGetValue(snapshot.AccountCode, out var balance))
            {
                errors.Add($"{databaseName}: ОСВ не содержит счет {snapshot.AccountCode}, сохраненный в снимке периода.");
                continue;
            }

            CompareAmount(errors, databaseName, "ОСВ/снимок", snapshot.AccountCode, "Сальдо нач. Дт", snapshot.OpeningDebit, balance.OpeningDebit);
            CompareAmount(errors, databaseName, "ОСВ/снимок", snapshot.AccountCode, "Сальдо нач. Кт", snapshot.OpeningCredit, balance.OpeningCredit);
            CompareAmount(errors, databaseName, "ОСВ/снимок", snapshot.AccountCode, "Оборот Дт", snapshot.TurnoverDebit, balance.TurnoverDebit);
            CompareAmount(errors, databaseName, "ОСВ/снимок", snapshot.AccountCode, "Оборот Кт", snapshot.TurnoverCredit, balance.TurnoverCredit);
            CompareAmount(errors, databaseName, "ОСВ/снимок", snapshot.AccountCode, "Сальдо кон. Дт", snapshot.ClosingDebit, balance.ClosingDebit);
            CompareAmount(errors, databaseName, "ОСВ/снимок", snapshot.AccountCode, "Сальдо кон. Кт", snapshot.ClosingCredit, balance.ClosingCredit);
        }
    }

    private static async Task CompareSnapshotsWithGeneralLedgerAsync(
        AppDbContext context,
        string databaseName,
        IReadOnlyCollection<AccountTurnoverSnapshot> snapshots,
        List<string> errors)
    {
        var ledger = await new BalanceService(context).GetGeneralLedgerAsync(PeriodStart.Year);
        var ledgerByAccount = ledger
            .Where(item => ControlAccounts.Contains(item.AccountCode))
            .ToDictionary(item => item.AccountCode, StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in snapshots)
        {
            if (!ledgerByAccount.TryGetValue(snapshot.AccountCode, out var ledgerRow))
            {
                errors.Add($"{databaseName}: главная книга не содержит счет {snapshot.AccountCode}, сохраненный в снимке периода.");
                continue;
            }

            CompareAmount(errors, databaseName, "Главная книга/снимок", snapshot.AccountCode, "Сальдо нач. Дт", snapshot.OpeningDebit, ledgerRow.OpeningDebit);
            CompareAmount(errors, databaseName, "Главная книга/снимок", snapshot.AccountCode, "Сальдо нач. Кт", snapshot.OpeningCredit, ledgerRow.OpeningCredit);
            CompareAmount(errors, databaseName, "Главная книга/снимок", snapshot.AccountCode, "Год Дт", snapshot.TurnoverDebit, ledgerRow.YearTurnoverDebit);
            CompareAmount(errors, databaseName, "Главная книга/снимок", snapshot.AccountCode, "Год Кт", snapshot.TurnoverCredit, ledgerRow.YearTurnoverCredit);
            CompareAmount(errors, databaseName, "Главная книга/снимок", snapshot.AccountCode, "Сальдо кон. Дт", snapshot.ClosingDebit, ledgerRow.ClosingDebit);
            CompareAmount(errors, databaseName, "Главная книга/снимок", snapshot.AccountCode, "Сальдо кон. Кт", snapshot.ClosingCredit, ledgerRow.ClosingCredit);
        }
    }

    private static async Task<int> CleanupControlDataAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var deletedPostings = await context.Database.ExecuteSqlRawAsync(@"
            DELETE FROM doc_postings
            WHERE doc_number LIKE @prefix;",
            new NpgsqlParameter("@prefix", TestDocumentPrefix + "%"));

        var deletedOpeningBalances = await context.AccountOpeningBalances
            .Where(item =>
                (item.BalanceDate == DateTime.SpecifyKind(OpeningBalanceDate, DateTimeKind.Utc) ||
                 item.BalanceDate == DateTime.SpecifyKind(PreviousControlOpeningBalanceDate, DateTimeKind.Utc)) &&
                ControlAccounts.Contains(item.AccountCode))
            .ExecuteDeleteAsync(cancellationToken);

        var periods = await context.AccountingPeriods
            .Where(item =>
                item.StartDate == DateTime.SpecifyKind(PeriodStart, DateTimeKind.Utc) &&
                item.EndDate == DateTime.SpecifyKind(PeriodEnd, DateTimeKind.Utc))
            .ToListAsync(cancellationToken);
        var periodIds = periods.Select(item => item.Id).ToList();
        var deletedStates = periodIds.Count == 0
            ? 0
            : await context.AccountingPeriodModuleStates
                .Where(item => periodIds.Contains(item.PeriodId))
                .ExecuteDeleteAsync(cancellationToken);
        var deletedSnapshots = periodIds.Count == 0
            ? 0
            : await context.AccountTurnoverSnapshots
                .Where(item => periodIds.Contains(item.PeriodId))
                .ExecuteDeleteAsync(cancellationToken);

        context.AccountingPeriods.RemoveRange(periods);
        var deletedPeriods = periods.Count;

        var controlModule = await context.MetadataModules
            .FirstOrDefaultAsync(item => item.Code == ControlModuleCode, cancellationToken);
        var deletedModules = 0;
        if (controlModule != null)
        {
            context.MetadataModules.Remove(controlModule);
            deletedModules = 1;
        }

        await context.SaveChangesAsync(cancellationToken);
        return deletedPostings + deletedOpeningBalances + deletedStates + deletedSnapshots + deletedPeriods + deletedModules;
    }

    private static AccountingPeriodModuleStatus FindModule(
        IReadOnlyCollection<AccountingPeriodModuleStatus> modules,
        string code,
        string name)
    {
        return modules.FirstOrDefault(item => item.ModuleCode.Equals(code, StringComparison.OrdinalIgnoreCase)) ??
               modules.FirstOrDefault(item => item.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase)) ??
               throw new InvalidOperationException($"Модуль «{name}» не найден в списке закрытия периода.");
    }

    private static async Task ExpectFailureAsync(
        Func<Task> action,
        string ruleDescription,
        string databaseName,
        List<string> errors)
    {
        try
        {
            await action();
            errors.Add($"{databaseName}: правило не сработало: {ruleDescription}.");
        }
        catch (InvalidOperationException)
        {
        }
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
}
