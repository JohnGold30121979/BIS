using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Результат приведения ссылочных колонок к uuid.
    /// </summary>
    public sealed class ReferenceColumnMigrationResult
    {
        /// <summary>Что переведено: «таблица.колонка (N строк)».</summary>
        public List<string> Converted { get; } = new();

        /// <summary>Что не удалось перевести и почему (данные при этом не тронуты).</summary>
        public List<string> Problems { get; } = new();
    }

    /// <summary>
    /// Приводит ссылочные колонки legacy-баз к uuid (шаг 3.3 «Модели данных»:
    /// «GUID-ссылки: из text в uuid»).
    ///
    /// Старые базы создавались до правила «Reference → uuid» и хранили ссылки в text:
    /// либо Id строкой, либо кодом записи («14160000»). Здесь text → uuid с переносом
    /// кода в Id по коду справочника (для плана счетов — по `code`, для валют — по `code`).
    ///
    /// Главный принцип: данные важнее схемы. Колонка переводится только если ВСЕ
    /// значения разобрались (Guid или код из справочника); иначе — откат и запись
    /// в отчёт, колонка остаётся как была. Шаг идемпотентен: уже uuid-колонки
    /// пропускаются, повторный запуск ничего не делает.
    ///
    /// Список колонок намеренно короткий и пополняется после проверки. Массовый
    /// перевод «всех text-ссылок» ломает сырой SQL, который передаёт в такие колонки
    /// строковые параметры (например, doc_postings.organization_id): это отдельная
    /// ревизия запросов, а не одна миграция.
    /// </summary>
    public sealed class ReferenceColumnMigrationService
    {
        /// <summary>
        /// Проверенные колонки: таблица, колонка. Для каждой сервис сам находит
        /// справочник по метаданным поля и переводит код в Id.
        /// </summary>
        private static readonly (string Table, string Column)[] VerifiedTargets =
        {
            ("catalog_advance_payments", "debit_account"),
            ("catalog_advance_payments", "credit_account"),
            ("catalog_advance_payments", "currency_id")
        };

        private const string GuidPattern =
            "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";

        private readonly AppDbContext _context;

        public ReferenceColumnMigrationService(AppDbContext context) => _context = context;

        public async Task<ReferenceColumnMigrationResult> ConvertAsync()
        {
            var result = new ReferenceColumnMigrationResult();

            // Быстрый путь: сначала только типы целевых колонок (1 запрос на таблицу).
            // Шаг повторяемый и выполняется при каждом открытии базы — читать ради
            // него все метаданные с полями (сотни строк по сети) нельзя. Как только
            // все целевые колонки уже uuid, уходим, не загружая метаданные.
            var columnTypesByTable = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var requiresWork = false;

            foreach (var tableName in VerifiedTargets
                         .Select(item => item.Table)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var columnTypes = await LoadColumnTypesAsync(tableName);
                columnTypesByTable[tableName] = columnTypes;

                foreach (var (table, column) in VerifiedTargets)
                {
                    if (!string.Equals(table, tableName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (columnTypes.TryGetValue(column, out var dataType) && IsTextColumn(dataType))
                        requiresWork = true;
                }
            }

            if (!requiresWork)
                return result;

            var objects = await _context.MetadataObjects
                .AsNoTracking()
                .Include(item => item.Fields)
                .ToListAsync();

            var fieldsByTable = objects
                .Where(item => !string.IsNullOrWhiteSpace(item.TableName))
                .GroupBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.SelectMany(item => item.Fields).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var (table, column) in VerifiedTargets)
            {
                try
                {
                    var columnTypes = columnTypesByTable[table];

                    if (columnTypes.Count == 0)
                        continue; // таблицы ещё нет — её создадут по метаданным

                    if (!columnTypes.TryGetValue(column, out var dataType))
                        continue; // колонки нет

                    if (string.Equals(dataType, "uuid", StringComparison.OrdinalIgnoreCase))
                        continue; // уже приведена

                    if (!IsTextColumn(dataType))
                    {
                        result.Problems.Add($"{table}.{column}: тип {dataType}, ожидался text/varchar — пропущено");
                        continue;
                    }

                    var field = fieldsByTable.TryGetValue(table, out var fields)
                        ? fields.FirstOrDefault(item =>
                            string.Equals(item.DbColumnName, column, StringComparison.OrdinalIgnoreCase))
                        : null;

                    var mapping = await LoadValueMappingAsync(field);
                    await ConvertColumnAsync(table, column, mapping, result);
                }
                catch (Exception ex)
                {
                    result.Problems.Add($"{table}.{column}: {ex.Message}");
                    SystemLogService.Warning(
                        $"Схема: не удалось перевести {table}.{column} в uuid — {ex.Message}",
                        nameof(ReferenceColumnMigrationService));
                }
            }

            return result;
        }

        /// <summary>
        /// text → uuid для одной колонки. Всё в транзакции: если хоть одно значение
        /// не разобралось, изменения откатываются и данные остаются нетронутыми.
        /// </summary>
        private async Task ConvertColumnAsync(
            string tableName,
            string columnName,
            ReferenceValueMap? mapping,
            ReferenceColumnMigrationResult result)
        {
            var quotedTable = Quote(tableName);
            var quotedColumn = Quote(columnName);
            var tempColumnName = columnName + "__uuid_migr";
            var quotedTemp = Quote(tempColumnName);

            var isNullable = await IsColumnNullableAsync(tableName, columnName);
            var rowCount = await ScalarLongAsync($"SELECT count(*) FROM {quotedTable}");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Остаток от прерванного запуска не должен мешать.
                await ExecuteAsync($"ALTER TABLE {quotedTable} DROP COLUMN IF EXISTS {quotedTemp};");
                await ExecuteAsync($"ALTER TABLE {quotedTable} ADD COLUMN {quotedTemp} uuid;");

                var valueExpression = mapping == null
                    ? $"CASE WHEN COALESCE(btrim({quotedColumn}::text), '') = '' THEN NULL " +
                      $"ELSE btrim({quotedColumn}::text)::uuid END"
                    : $@"CASE
                            WHEN COALESCE(btrim({quotedColumn}::text), '') = '' THEN NULL
                            WHEN btrim({quotedColumn}::text) ~ @guidPattern THEN btrim({quotedColumn}::text)::uuid
                            ELSE (
                                SELECT target.{Quote(mapping.IdColumn)}
                                FROM {Quote(mapping.TableName)} target
                                WHERE COALESCE(btrim(target.{Quote(mapping.CodeColumn)}::text), '') =
                                      btrim({quotedColumn}::text)
                                LIMIT 1
                            )
                        END";

                await ExecuteAsync(
                    $"UPDATE {quotedTable} SET {quotedTemp} = {valueExpression};",
                    ("@guidPattern", GuidPattern));

                // Проверка: ни одно непустое значение не потерялось.
                var unresolved = await ScalarLongAsync(
                    $"SELECT count(*) FROM {quotedTable} " +
                    $"WHERE COALESCE(btrim({quotedColumn}::text), '') <> '' AND {quotedTemp} IS NULL;");
                if (unresolved > 0)
                {
                    await transaction.RollbackAsync();
                    result.Problems.Add(
                        $"{tableName}.{columnName}: не удалось разобрать {unresolved} знач. — колонка оставлена как была");
                    return;
                }

                if (!isNullable)
                {
                    var nulls = await ScalarLongAsync($"SELECT count(*) FROM {quotedTable} WHERE {quotedTemp} IS NULL;");
                    if (nulls > 0)
                    {
                        await transaction.RollbackAsync();
                        result.Problems.Add(
                            $"{tableName}.{columnName}: NOT NULL, но {nulls} пустых знач. — колонка оставлена как была");
                        return;
                    }
                }

                // Меняем колонку местами: старую text-колонку удаляем, новую uuid
                // переименовываем в исходное имя. Зависимости (индексы/вьюхи) не
                // сносим каскадом: если DROP не прошёл, транзакция откатится.
                await ExecuteAsync($"ALTER TABLE {quotedTable} DROP COLUMN {quotedColumn};");
                await ExecuteAsync(
                    $"ALTER TABLE {quotedTable} RENAME COLUMN {quotedTemp} TO {quotedColumn};");

                if (!isNullable)
                {
                    await ExecuteAsync(
                        $"ALTER TABLE {quotedTable} ALTER COLUMN {quotedColumn} SET NOT NULL;");
                }

                await transaction.CommitAsync();
                result.Converted.Add($"{tableName}.{columnName} (строк: {rowCount})");
                SystemLogService.Info(
                    $"Схема: {tableName}.{columnName} приведена к uuid (строк: {rowCount}).",
                    nameof(ReferenceColumnMigrationService));
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Куда переводить коды: справочник поля и его кодовая колонка. Для «Плана
        /// счетов» это catalog_plan_schetov.code, для валют — catalog_currencies.code.
        /// Если справочника/кода нет, колонка переводится только при условии, что
        /// все значения уже Guid (иначе откат).
        /// </summary>
        private async Task<ReferenceValueMap?> LoadValueMappingAsync(MetadataField? field)
        {
            var catalogName = field?.ReferenceCatalog;
            if (string.IsNullOrWhiteSpace(catalogName))
                return null;

            var catalog = await _context.MetadataObjects
                .AsNoTracking()
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Catalog" &&
                    item.Name == catalogName);

            if (catalog == null || string.IsNullOrWhiteSpace(catalog.TableName))
                return null;

            var codeField = catalog.Fields.FirstOrDefault(item =>
                                 string.Equals(item.DbColumnName, "code", StringComparison.OrdinalIgnoreCase))
                             ?? catalog.Fields.FirstOrDefault(item =>
                                 string.Equals(item.Name, "Код", StringComparison.OrdinalIgnoreCase));

            if (codeField == null || string.IsNullOrWhiteSpace(codeField.DbColumnName))
                return null;

            return new ReferenceValueMap(catalog.TableName, "Id", codeField.DbColumnName);
        }

        private async Task<bool> IsColumnNullableAsync(string tableName, string columnName)
        {
            var connection = _context.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere)
                await _context.Database.OpenConnectionAsync();

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT is_nullable
                    FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = @table AND column_name = @column;";

                var tableParameter = command.CreateParameter();
                tableParameter.ParameterName = "@table";
                tableParameter.Value = tableName;
                command.Parameters.Add(tableParameter);

                var columnParameter = command.CreateParameter();
                columnParameter.ParameterName = "@column";
                columnParameter.Value = columnName;
                command.Parameters.Add(columnParameter);

                var value = await command.ExecuteScalarAsync();
                return value == null || value == DBNull.Value ||
                       string.Equals(value.ToString(), "YES", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (openedHere)
                    await _context.Database.CloseConnectionAsync();
            }
        }

        private async Task<long> ScalarLongAsync(string sql)
        {
            var connection = _context.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere)
                await _context.Database.OpenConnectionAsync();

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                var value = await command.ExecuteScalarAsync();
                return value == null || value == DBNull.Value ? 0L : Convert.ToInt64(value);
            }
            finally
            {
                if (openedHere)
                    await _context.Database.CloseConnectionAsync();
            }
        }

        /// <summary>
        /// DDL/DML без EF-форматирования: строки содержат регулярные выражения с
        /// фигурными скобками, а ExecuteSqlRaw с параметрами прогоняет их через
        /// string.Format («Index (zero based)…»). Параметры передаются как есть,
        /// команда выполняется на том же соединении и попадает в текущую транзакцию.
        /// </summary>
        private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            var connection = _context.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere)
                await _context.Database.OpenConnectionAsync();

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;

                foreach (var (name, value) in parameters)
                    command.Parameters.Add(new NpgsqlParameter(name, value));

                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                if (openedHere)
                    await _context.Database.CloseConnectionAsync();
            }
        }

        private async Task<Dictionary<string, string>> LoadColumnTypesAsync(string tableName)
        {
            var connection = _context.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere)
                await _context.Database.OpenConnectionAsync();

            try
            {
                return await DbValueConverter.LoadColumnTypesAsync(connection, tableName);
            }
            finally
            {
                if (openedHere)
                    await _context.Database.CloseConnectionAsync();
            }
        }

        private static bool IsTextColumn(string dataType)
        {
            return dataType.Equals("text", StringComparison.OrdinalIgnoreCase) ||
                   dataType.StartsWith("character varying", StringComparison.OrdinalIgnoreCase) ||
                   dataType.StartsWith("character", StringComparison.OrdinalIgnoreCase);
        }

        private static string Quote(string identifier) =>
            "\"" + (identifier ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private sealed record ReferenceValueMap(string TableName, string IdColumn, string CodeColumn);
    }
}
