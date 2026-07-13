using System.Data;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

public enum SmokeTestCommand
{
    Verify,
    Run,
    Cleanup
}

public sealed record SmokeTestResult(
    bool IsSuccess,
    string Summary,
    IReadOnlyCollection<string> Details)
{
    public static SmokeTestResult Success(string summary, params string[] details) =>
        new(true, summary, details);

    public static SmokeTestResult Failure(string summary, IEnumerable<string>? details = null) =>
        new(false, summary, (details ?? Array.Empty<string>()).ToArray());
}

public sealed class SmokeTestRunOptions
{
    public SmokeTestCommand Command { get; init; } = SmokeTestCommand.Verify;
    public int CycleCount { get; init; } = 1;
    public int DocumentCount { get; init; } = 1;
    public string? OperationsFilePath { get; init; }
    public IReadOnlyCollection<SmokeTestOperation> Operations { get; init; } = Array.Empty<SmokeTestOperation>();
}

public sealed class SmokeTestOperation
{
    public string DocumentKind { get; set; } = "Sales";
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1m;
    public decimal AmountWithoutTax { get; set; } = 1000m;
    public decimal VatRate { get; set; } = 12m;
    public decimal SalesTaxRate { get; set; } = 1.5m;
    public string CounterpartyAccountCode { get; set; } = string.Empty;
    public string LineAccountCode { get; set; } = string.Empty;
    public string PaymentKind { get; set; } = "TRANSFER";
    public string DeliveryKind { get; set; } = "GOODS";
    public string SupplyKind { get; set; } = "TAXABLE";
    public string Basis { get; set; } = string.Empty;
    public string TaxBlankNumber { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = "FIN";
}

public interface ISmokeTestScenario
{
    string Code { get; }
    string Name { get; }
    string Category { get; }
    string Description { get; }
    bool SupportsVerify { get; }
    bool SupportsRun { get; }
    bool SupportsCleanup { get; }

    Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public static class SmokeTestRegistry
{
    public static IReadOnlyList<ISmokeTestScenario> DiscoverScenarios()
    {
        return typeof(SmokeTestRegistry).Assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                typeof(ISmokeTestScenario).IsAssignableFrom(type) &&
                type.GetConstructor(Type.EmptyTypes) != null)
            .Select(type => (ISmokeTestScenario)Activator.CreateInstance(type)!)
            .OrderBy(type => type.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static ISmokeTestScenario? FindByCode(string code)
    {
        return DiscoverScenarios()
            .FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    }
}

public abstract class SmokeTestScenarioBase : ISmokeTestScenario
{
    protected SmokeTestScenarioBase()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    public abstract string Code { get; }
    public abstract string Name { get; }
    public abstract string Category { get; }
    public abstract string Description { get; }
    public virtual bool SupportsVerify => true;
    public virtual bool SupportsRun => true;
    public virtual bool SupportsCleanup => true;

    public abstract Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    protected static void Report(IProgress<string>? progress, string message)
    {
        progress?.Report(message);
    }

    internal static TestSettings LoadSettings()
    {
        return TestEnvironment.LoadSettings();
    }

    internal static async Task<List<DatabaseCandidate>> LoadDatabaseCandidatesAsync(
        TestSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<DatabaseCandidate>
        {
            new("Текущая база из appsettings", settings.DatabaseName, settings.ConnectionString(settings.DatabaseName))
        };

        try
        {
            await using var connection = new NpgsqlConnection(settings.ConnectionString(settings.DatabaseName));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ""Name"", ""DatabaseName"", ""Host"", ""Port"", ""Username"", ""Password""
                FROM ""InfoBases""
                ORDER BY ""IsActive"" DESC, ""CreatedAt"" DESC;";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                var databaseName = reader.GetString(1);
                var host = reader.GetString(2);
                var port = reader.GetInt32(3);
                var username = reader.GetString(4);
                var password = reader.GetString(5);
                var connectionString = $"Host={host};Port={port};Database={databaseName};Username={username};Password={password}";

                if (result.All(item => !string.Equals(item.DatabaseName, databaseName, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new(name, databaseName, connectionString));
            }
        }
        catch (Exception ex)
        {
            Report(progress, $"INFO: список информационных баз не прочитан: {ex.Message}");
        }

        return result;
    }

    internal static async Task<bool> HasRequiredTableAsync(
        AppDbContext context,
        string tableName,
        CancellationToken cancellationToken = default)
    {
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
            if (opened)
                await connection.CloseAsync();
        }
    }

    internal static string NormalizeLegacyDocumentNumber(string? documentNumber)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
            return string.Empty;

        var normalizedNumber = documentNumber.Trim();
        if (normalizedNumber.Any(char.IsLetter))
            return normalizedNumber;

        var digitsOnly = new string(normalizedNumber.Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digitsOnly) ? normalizedNumber : digitsOnly;
    }

    internal static void DeleteDirectorySafe(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            // Временные файлы не должны ронять тесты.
        }
    }

    internal static void DeleteFileSafe(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Служебный файл не должен ронять тесты.
        }
    }
}

public sealed class SqlQueryConsoleService
{
    public SqlQueryConsoleService()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    public async Task<IReadOnlyList<DatabaseCandidate>> LoadCandidatesAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = SmokeTestScenarioBase.LoadSettings();
        return await SmokeTestScenarioBase.LoadDatabaseCandidatesAsync(settings, progress, cancellationToken);
    }

    public async Task<ConnectionCheckResult> TestConnectionAsync(
        TestSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(settings.ConnectionString(settings.DatabaseName));
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "select current_database(), current_user;";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new ConnectionCheckResult(
                    true,
                    $"Подключение успешно: база {reader.GetString(0)}, пользователь {reader.GetString(1)}.",
                    reader.GetString(0),
                    reader.GetString(1));
            }

            return new ConnectionCheckResult(true, "Подключение успешно.");
        }
        catch (Exception ex)
        {
            return new ConnectionCheckResult(false, $"Ошибка подключения: {ex.Message}");
        }
    }

    public async Task<SqlQueryExecutionResult> ExecuteAsync(
        DatabaseCandidate candidate,
        string sql,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("Текст SQL-запроса пустой.");

        var startedAt = DateTime.Now;
        await using var connection = new NpgsqlConnection(candidate.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 0;

        var table = new DataTable();
        var hasRows = false;
        var recordsAffected = -1;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        do
        {
            if (!hasRows && reader.FieldCount > 0)
            {
                table.Load(reader);
                hasRows = table.Columns.Count > 0;
            }
        }
        while (await reader.NextResultAsync(cancellationToken));

        recordsAffected = reader.RecordsAffected;

        var duration = DateTime.Now - startedAt;
        if (hasRows)
        {
            return new SqlQueryExecutionResult(
                true,
                candidate.DatabaseName,
                $"Получено строк: {table.Rows.Count}. Время: {duration.TotalMilliseconds:N0} мс.",
                table,
                table.Rows.Count,
                duration);
        }

        return new SqlQueryExecutionResult(
            true,
            candidate.DatabaseName,
            $"Команда выполнена. Затронуто строк: {recordsAffected}. Время: {duration.TotalMilliseconds:N0} мс.",
            null,
            recordsAffected,
            duration);
    }
}

public sealed record TestSettings(
    string Host = "localhost",
    int Port = 5432,
    string DatabaseName = "bis_master",
    string Username = "postgres",
    string Password = "qwerty123")
{
    public string ConnectionString(string databaseName)
    {
        return $"Host={Host};Port={Port};Database={databaseName};Username={Username};Password={Password}";
    }
}

public sealed record DatabaseCandidate(string Name, string DatabaseName, string ConnectionString)
{
    public override string ToString() => $"{Name} ({DatabaseName})";
}

public sealed record SqlQueryExecutionResult(
    bool IsSuccess,
    string DatabaseName,
    string Message,
    DataTable? Table,
    int RecordsAffected,
    TimeSpan Duration);

public static class SqlNames
{
    public static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
