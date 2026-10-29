using BIS.ERP.Data;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;

namespace BIS.ERP.Testing;

/// <summary>
/// Шаг 3.3 «GUID-ссылки: из text в uuid» для справочника «Пары счетов».
///
/// Проверяет на базе из testsettings, что миграция (SchemaMigrationService →
/// ReferenceColumnMigrationService):
///   * переводит legacy text-колонки debit_account/credit_account/currency_id в uuid;
///   * переносит коды счетов («14160000») в Id по плану счетов;
///   * не теряет строки и значения (Guid остаётся Guid, пустое — NULL);
///   * идемпотентна: повторный запуск ничего не меняет.
///
/// Сценарий работает только с базой из testsettings, чтобы случайно не пройтись
/// миграцией по всем зарегистрированным информационным базам.
/// </summary>
public sealed class PairsReferenceMigrationScenario : SmokeTestScenarioBase
{
    private const string PairsTable = "catalog_advance_payments";
    private const string ChartTable = "catalog_plan_schetov";

    private static readonly string[] SubjectColumns = { "debit_account", "credit_account", "currency_id" };

    private const string GuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";

    public override string Code => "schema-pairs-uuid";
    public override string Name => "Схема: пары счетов → uuid";
    public override string Category => "Схема";
    public override string Description =>
        "Проверяет миграцию ссылочных колонок «Пар счетов»: text → uuid с переносом кодов счетов в Id.";
    public override bool SupportsRun => false;
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var details = new List<string>();
        var errors = new List<string>();

        var settings = LoadSettings();
        var candidate = new DatabaseCandidate(
            "testsettings",
            settings.DatabaseName,
            settings.ConnectionString(settings.DatabaseName));

        Report(progress, $"Проверяется база: {candidate.DatabaseName}");

        await using var context = new AppDbContext(candidate.ConnectionString);
        if (!await HasRequiredTableAsync(context, "MetadataObjects", cancellationToken))
        {
            return SmokeTestResult.Failure(
                $"{candidate.DatabaseName}: это не информационная база BIS ERP — нет MetadataObjects.");
        }

        var before = await LoadSnapshotAsync(context, cancellationToken);
        if (!before.TableExists)
        {
            return SmokeTestResult.Failure(
                $"{candidate.DatabaseName}: нет таблицы {PairsTable} — «Пары счетов» не созданы.");
        }

        details.Add(before.ColumnTypes.TryGetValue("debit_account", out var debitType)
            ? $"До миграции: debit_account = {debitType}, строк {before.Rows.Count}."
            : $"До миграции: строк {before.Rows.Count}.");

        // Та же миграция, что выполняется при открытии базы в приложении.
        await EnsureDatabaseSchemaAsync(candidate, cancellationToken);

        await using var afterContext = new AppDbContext(candidate.ConnectionString);
        var after = await LoadSnapshotAsync(afterContext, cancellationToken);

        if (after.Rows.Count != before.Rows.Count)
        {
            errors.Add($"Количество пар изменилось: было {before.Rows.Count}, стало {after.Rows.Count}.");
        }

        foreach (var column in SubjectColumns)
        {
            if (!after.ColumnTypes.TryGetValue(column, out var dataType))
            {
                errors.Add($"После миграции нет колонки {column}.");
                continue;
            }

            if (!string.Equals(dataType, "uuid", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{column}: тип {dataType}, ожидался uuid.");
        }

        foreach (var row in before.Rows)
        {
            if (!after.Rows.TryGetValue(row.Key, out var afterRow))
            {
                errors.Add($"Пара «{row.Key}» пропала после миграции.");
                continue;
            }

            CheckValue(before, after, row.Key, "debit_account", row.Value.Debit, afterRow.Debit, errors);
            CheckValue(before, after, row.Key, "credit_account", row.Value.Credit, afterRow.Credit, errors);
            CheckValue(before, after, row.Key, "currency_id", row.Value.Currency, afterRow.Currency, errors);
        }

        // Идемпотентность: повторный прогон не должен ничего «переводить».
        var secondRun = await new ReferenceColumnMigrationService(afterContext).ConvertAsync();
        if (secondRun.Converted.Count > 0)
        {
            errors.Add("Повторный запуск снова что-то переводил: " + string.Join("; ", secondRun.Converted));
        }

        details.Add(secondRun.Problems.Count == 0
            ? "Повторный запуск: изменений нет (идемпотентно)."
            : "Повторный запуск: " + string.Join("; ", secondRun.Problems));

        return errors.Count == 0
            ? SmokeTestResult.Success("Миграция «Пары счетов» → uuid проверена.", details.ToArray())
            : SmokeTestResult.Failure("Миграция «Пары счетов» → uuid выявила ошибки.", errors.Concat(details));
    }

    private static void CheckValue(
        PairsSnapshot before,
        PairsSnapshot after,
        string pairCode,
        string column,
        string? beforeValue,
        string? afterValue,
        List<string> errors)
    {
        var wasEmpty = string.IsNullOrWhiteSpace(beforeValue);
        var beforeText = beforeValue?.Trim() ?? string.Empty;

        if (wasEmpty)
        {
            if (!string.IsNullOrWhiteSpace(afterValue))
                errors.Add($"Пара {pairCode}, {column}: пустое значение превратилось в «{afterValue}».");
            return;
        }

        string expected;
        if (GuidPatternMatches(beforeText))
        {
            expected = beforeText.ToLowerInvariant();
        }
        else if (before.AccountIdsByCode.TryGetValue(beforeText, out var mapped))
        {
            expected = mapped.ToLowerInvariant();
        }
        else
        {
            errors.Add($"Пара {pairCode}, {column}: код «{beforeText}» не найден в плане счетов.");
            return;
        }

        if (!string.Equals(afterValue?.Trim(), expected, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"Пара {pairCode}, {column}: было «{beforeText}», стало «{afterValue}», ожидался Id «{expected}».");
        }
    }

    private static bool GuidPatternMatches(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, GuidPattern);

    private static async Task<PairsSnapshot> LoadSnapshotAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var snapshot = new PairsSnapshot();

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using (var typesCommand = connection.CreateCommand())
            {
                typesCommand.CommandText = @"
                    SELECT column_name, data_type
                    FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = @table;";
                typesCommand.Parameters.AddWithValue("@table", PairsTable);

                await using var reader = await typesCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    snapshot.ColumnTypes[reader.GetString(0)] = reader.GetString(1);
            }

            snapshot.TableExists = snapshot.ColumnTypes.Count > 0;
            if (!snapshot.TableExists)
                return snapshot;

            await using (var chartCommand = connection.CreateCommand())
            {
                chartCommand.CommandText = $@"
                    SELECT btrim(code), ""Id""::text
                    FROM ""{ChartTable}""
                    WHERE COALESCE(btrim(code), '') <> '';";

                await using var reader = await chartCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    snapshot.AccountIdsByCode[reader.GetString(0)] = reader.GetString(1);
            }

            await using (var pairsCommand = connection.CreateCommand())
            {
                pairsCommand.CommandText = $@"
                    SELECT code, name, debit_account::text, credit_account::text, currency_id::text
                    FROM ""{PairsTable}""
                    ORDER BY code;";

                await using var reader = await pairsCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var code = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    snapshot.Rows[code] = new PairsRow(
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4));
                }
            }

            return snapshot;
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }

    private sealed class PairsSnapshot
    {
        public bool TableExists { get; set; }
        public Dictionary<string, string> ColumnTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> AccountIdsByCode { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, PairsRow> Rows { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record PairsRow(string? Debit, string? Credit, string? Currency);
}
