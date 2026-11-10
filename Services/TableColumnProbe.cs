using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Проверка наличия колонки в таблице — без изменения схемы базы.
    ///
    /// Зачем: колонка <c>line_analytics</c> появилась позже самих таблиц строк.
    /// В базах, созданных раньше, её нет, а SELECT/INSERT её упоминали — приходила
    /// ошибка PostgreSQL 42703 («столбец "line_analytics" не существует»), и
    /// открытие документа падало. Здесь база только читается: спрашиваем
    /// information_schema и строим SQL без отсутствующей колонки.
    ///
    /// Результат кэшируется на сеанс: ключ — строка подключения + таблица + колонка,
    /// поэтому каталог не дёргается на каждое открытие документа и кэш не путается
    /// при переключении инфобазы.
    /// </summary>
    public static class TableColumnProbe
    {
        private static readonly Dictionary<string, bool> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new();

        public static async Task<bool> HasColumnAsync(
            NpgsqlConnection connection,
            string table,
            string column,
            NpgsqlTransaction? transaction = null)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column))
                return false;

            var key = $"{connection.ConnectionString}|{table}|{column}";
            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out var cached))
                    return cached;
            }

            await using var command = connection.CreateCommand();
            if (transaction != null)
                command.Transaction = transaction;

            command.CommandText = @"
                SELECT EXISTS (
                    SELECT 1
                      FROM information_schema.columns
                     WHERE table_schema = 'public'
                       AND table_name = @table
                       AND column_name = @column);";
            command.Parameters.Add(new NpgsqlParameter("table", table));
            command.Parameters.Add(new NpgsqlParameter("column", column));

            var exists = await command.ExecuteScalarAsync() is true;

            lock (CacheLock)
                Cache[key] = exists;

            if (!exists)
            {
                // Одно предупреждение на таблицу и колонку: дальше работает кэш.
                SystemLogService.Warning(
                    $"В таблице «{table}» нет колонки «{column}»: данные читаются и пишутся без неё, " +
                    "схема базы не изменяется.",
                    "Схема");
            }

            return exists;
        }
    }
}
