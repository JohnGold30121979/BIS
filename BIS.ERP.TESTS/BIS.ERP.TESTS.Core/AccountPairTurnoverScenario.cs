using BIS.ERP.Data;
using BIS.ERP.Services;
using Npgsql;

namespace BIS.ERP.Testing;

/// <summary>Verify: контрольные расчёты и вывод. Run: дополнительно чтение доступных баз.</summary>
public sealed class AccountPairTurnoverScenario : SmokeTestScenarioBase
{
    public override string Code => "account-pair-turnover";
    public override string Name => "Ведомость по паре счетов: расчёт, Excel и PDF";
    public override string Category => "Финансовые отчёты";
    public override string Description => "Проверяет обе стороны пары, остатки, отбор организаций и печатный макет. Run дополнительно проверяет SQL на базах только для чтения.";
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(SmokeTestRunOptions options,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (options.Command == SmokeTestCommand.Cleanup)
            return SmokeTestResult.Failure("Сценарий не создаёт данные в БД и не требует очистки.");
        var details = new List<string>();
        var errors = new List<string>();
        var output = Path.Combine(TestEnvironment.GetLogsDirectory(), "account-pair-turnover");
        try
        {
            var count = AccountPairTurnoverChecks.Run(output, message => { details.Add(message); Report(progress, message); });
            details.Add($"Контрольных проверок: {count}. Артефакты: {output}");
        }
        catch (Exception ex)
        {
            return SmokeTestResult.Failure("Ошибка контрольного расчёта ведомости.", details.Append(DescribeError(ex)));
        }
        if (options.Command == SmokeTestCommand.Run)
        {
            var candidates = await LoadDatabaseCandidatesAsync(LoadSettings(), progress, cancellationToken);
            var pairCount = 0;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var connection = new NpgsqlConnectionStringBuilder(candidate.ConnectionString)
                        { Options = "-c default_transaction_read_only=on" };
                    await using var context = new AppDbContext(connection.ToString());
                    if (!await HasRequiredTableAsync(context, "doc_postings", cancellationToken)) continue;
                    var pairs = await AccountPairTurnoverBuilder.LoadPairsAsync(new MetadataService(context));
                    foreach (var pair in pairs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var data = await AccountPairTurnoverBuilder.LoadAsync(context, pair, null,
                            new DateTime(DateTime.Today.Year, 1, 1), DateTime.Today);
                        if (!data.Columns.Contains("Модуль")) throw new InvalidOperationException("Нет колонки Модуль.");
                        pairCount++;
                        details.Add($"{candidate.DatabaseName}: пара {pair.Code}, строк {data.Rows.Count}, read-only.");
                    }
                }
                catch (Exception ex) { errors.Add(DescribeError(ex, candidate)); }
            }
            if (pairCount == 0) errors.Add("Не проверено ни одной пары на доступных базах.");
        }
        return errors.Count == 0
            ? SmokeTestResult.Success("Ведомость по паре счетов: проверки пройдены.", details.ToArray())
            : SmokeTestResult.Failure("Ведомость по паре счетов: ошибки.", errors.Concat(details));
    }
}
