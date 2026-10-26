using System.Data;
using BIS.ERP.Data;
using BIS.ERP.Services;
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

    /// <summary>
    /// Фабрика контекста, привязанная к подключению проверяемой базы.
    ///
    /// Нельзя брать ServiceLocator.DbContextFactory: он отдаёт контекст «текущей»
    /// инфобазы приложения, которой в тестовом процессе нет — отсюда ошибка
    /// «No password has been provided».
    /// </summary>
    protected static IAppDbContextFactory CreateFactory(DatabaseCandidate candidate) =>
        new FixedConnectionDbContextFactory(candidate.ConnectionString);

    private sealed class FixedConnectionDbContextFactory : IAppDbContextFactory
    {
        private readonly string _connectionString;

        public FixedConnectionDbContextFactory(string connectionString) => _connectionString = connectionString;

        public Task<AppDbContext> CreateAsync() => Task.FromResult(new AppDbContext(_connectionString));
    }

    /// <summary>
    /// Понятное описание ошибки для лога: тип, сообщение, вложенная причина,
    /// место в коде и подсказка для типовых случаев.
    /// </summary>
    protected static string DescribeError(Exception ex, DatabaseCandidate? candidate = null)
    {
        var parts = new List<string> { $"{ex.GetType().Name}: {ex.Message}" };

        if (ex.InnerException != null)
            parts.Add($"причина: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");

        var text = ex.ToString();
        if (text.Contains("No password has been provided", StringComparison.OrdinalIgnoreCase))
            parts.Add("подсказка: в строке подключения нет пароля — проверьте поле «Пароль» в тест-центре и файл testsettings.json");
        else if (text.Contains("password authentication failed", StringComparison.OrdinalIgnoreCase))
            parts.Add("подсказка: пароль не подходит к этой базе");
        else if (text.Contains("does not exist", StringComparison.OrdinalIgnoreCase) &&
                 text.Contains("database", StringComparison.OrdinalIgnoreCase))
            parts.Add("подсказка: базы нет на сервере — проверьте имя базы");

        var frame = new System.Diagnostics.StackTrace(ex, true).GetFrames()?
            .FirstOrDefault(item => item.GetFileName()?.Contains("BIS.ERP", StringComparison.OrdinalIgnoreCase) == true);
        if (frame != null)
            parts.Add($"место: {System.IO.Path.GetFileName(frame.GetFileName())}:{frame.GetFileLineNumber()}");

        if (candidate != null)
            parts.Add($"база: {candidate.DatabaseName}");

        return string.Join(" | ", parts);
    }

    /// <summary>Строка диагностики подключения (без пароля) — пишется перед проверкой базы.</summary>
    protected static string DescribeConnection(DatabaseCandidate candidate)
    {
        if (!TestSettings.TryParseConnectionString(candidate.ConnectionString, out var settings))
            return $"{candidate.DatabaseName}: строку подключения разобрать не удалось";

        var passwordState = string.IsNullOrWhiteSpace(settings.Password) ? "НЕ ЗАДАН" : "задан";
        return $"{settings.Host}:{settings.Port}/{settings.DatabaseName}, пользователь {settings.Username}, пароль: {passwordState}";
    }

    /// <summary>
    /// Приводит схему тестовой базы к актуальной теми же миграциями, что и приложение:
    /// журнал schema_migrations, достройка колонок EF и метаданных.
    ///
    /// force: true обязателен — за один прогон несколько сценариев работают с одной базой,
    /// и без него второй сценарий молча пропустил бы миграции (статический барьер
    /// ReportedConnections в RuntimeSchemaFixService).
    /// Открывается собственное подключение: контекст сценария в это время занят.
    /// </summary>
    protected static async Task EnsureDatabaseSchemaAsync(
        DatabaseCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        await using var context = new AppDbContext(candidate.ConnectionString);
        await new RuntimeSchemaFixService(context).EnsureAsync(force: true);
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
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            Report(progress, $"INFO: сконфигурированная база «{settings.DatabaseName}» не найдена. Пробуем перечислить доступные базы.");
            await TryLoadDatabasesFromPostgresAsync(settings, result, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            Report(progress, $"INFO: список информационных баз не прочитан: {ex.Message}");
        }

        return result;
    }

    private static async Task TryLoadDatabasesFromPostgresAsync(
        TestSettings settings,
        List<DatabaseCandidate> result,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var fallbackDatabase = string.Equals(settings.DatabaseName, "postgres", StringComparison.OrdinalIgnoreCase)
            ? "template1"
            : "postgres";

        try
        {
            await using var connection = new NpgsqlConnection(settings.ConnectionString(fallbackDatabase));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT datname
                FROM pg_database
                WHERE datistemplate = false
                  AND datallowconn = true
                ORDER BY datname;";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var databaseName = reader.GetString(0);
                var connectionString = settings.ConnectionString(databaseName);
                var label = string.Equals(databaseName, settings.DatabaseName, StringComparison.OrdinalIgnoreCase)
                    ? "Текущая база из appsettings"
                    : databaseName;

                if (result.All(item => !string.Equals(item.DatabaseName, databaseName, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new(label, databaseName, connectionString));
            }

            if (result.Count == 1)
            {
                Report(progress, "INFO: на сервере не найдено доступных баз данных.");
            }
            else
            {
                Report(progress, $"INFO: найдено баз данных: {result.Count - 1}.");
            }
        }
        catch (Exception ex)
        {
            Report(progress, $"INFO: не удалось получить список баз через {fallbackDatabase}: {ex.Message}");
        }
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

    /// <summary>
    /// Снимок счетчиков нумерации документов (doc_numbering). Тесты создают
    /// документы штатным механизмом, а он увеличивает счетчики; после проверки
    /// счетчики возвращаются к прежним значениям, чтобы тестовые номера не
    /// «накручивали» нумерацию реальных документов.
    /// </summary>
    internal static async Task<Dictionary<Guid, int>> LoadDocumentNumberingStateAsync(
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, int>();
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
            command.CommandText = "SELECT id, current_number FROM doc_numbering;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result[reader.GetGuid(0)] = reader.GetInt32(1);
        }
        catch
        {
            // Таблицы нумерации может не быть в старых базах — тест продолжается.
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }

        return result;
    }

    /// <summary>
    /// Возвращает счетчики нумерации к снятым значениям. Возвращает число
    /// восстановленных строк.
    /// </summary>
    internal static async Task<int> RestoreDocumentNumberingStateAsync(
        AppDbContext context,
        IReadOnlyDictionary<Guid, int> state,
        CancellationToken cancellationToken = default)
    {
        if (state.Count == 0)
            return 0;

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        var restored = 0;
        try
        {
            foreach (var pair in state)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE doc_numbering
                    SET current_number = @currentNumber, UpdatedAt = NOW()
                    WHERE id = @id AND current_number <> @currentNumber;";
                command.Parameters.AddWithValue("@currentNumber", pair.Value);
                command.Parameters.AddWithValue("@id", pair.Key);
                restored += await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }

        return restored;
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
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            return new ConnectionCheckResult(
                false,
                $"База данных «{settings.DatabaseName}» не существует на сервере. " +
                $"Проверьте имя базы и создайте её, либо используйте существующую (например, postgres или bis_master).");
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

            // RecordsAffected читаем, пока reader открыт: после последнего NextResultAsync
            // Npgsql закрывает reader, и обращение к свойству даёт «The reader is closed».
            try
            {
                recordsAffected = reader.RecordsAffected;
            }
            catch (InvalidOperationException)
            {
                // reader уже закрыт — оставляем последнее известное значение
            }
        }
        while (await reader.NextResultAsync(cancellationToken));

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

    /// <summary>
    /// Разбирает строку подключения того же формата, что создаёт ConnectionString.
    /// </summary>
    public static bool TryParseConnectionString(string? connectionString, out TestSettings settings)
    {
        settings = new TestSettings();
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        string? host = null;
        int? port = null;
        string? databaseName = null;
        string? username = null;
        string? credential = null;

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = part[..separatorIndex].Trim();
            var value = part[(separatorIndex + 1)..].Trim();
            switch (key.ToLowerInvariant())
            {
                case "host":
                case "server":
                    host = value;
                    break;
                case "port":
                    if (int.TryParse(value, out var parsedPort))
                        port = parsedPort;
                    break;
                case "database":
                case "db":
                    databaseName = value;
                    break;
                case "username":
                case "user id":
                case "userid":
                    username = value;
                    break;
                case "password":
                    credential = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(databaseName))
            return false;

        settings = new TestSettings(
            host!,
            port ?? 5432,
            databaseName!,
            string.IsNullOrWhiteSpace(username) ? "postgres" : username!,
            credential ?? string.Empty);
        return true;
    }

    /// <summary>
    /// Строка подключения указывает на те же хост, порт, базу и пользователя.
    /// </summary>
    public bool MatchesConnectionString(string? connectionString)
    {
        return TryParseConnectionString(connectionString, out var other) &&
               string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase) &&
               Port == other.Port &&
               string.Equals(DatabaseName, other.DatabaseName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Username, other.Username, StringComparison.OrdinalIgnoreCase);
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
