using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>Данные колонки из information_schema.</summary>
    public sealed record ColumnInfo(string DataType, bool IsNullable, string? DefaultValue);

    /// <summary>
    /// Снимок схемы public-схемы: таблица → колонка → тип/обязательность/значение по умолчанию.
    ///
    /// Зачем: раньше каждый сервис спрашивал information_schema по одной таблице.
    /// На удалённой базе (VPN) это десятки round-trip'ов и медленное построение дерева,
    /// а на общем соединении EF такие запросы ещё и падали с
    /// NpgsqlOperationInProgressException («A command is already in progress»).
    ///
    /// Теперь схема читается ОДНИМ запросом на отдельном соединении и переиспользуется.
    /// </summary>
    public sealed class DatabaseSchemaCache
    {
        private readonly Dictionary<string, Dictionary<string, ColumnInfo>> _tables =
            new(StringComparer.OrdinalIgnoreCase);

        public static async Task<DatabaseSchemaCache> LoadAsync(string connectionString)
        {
            var cache = new DatabaseSchemaCache();

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT table_name, column_name, data_type, is_nullable, column_default
                FROM information_schema.columns
                WHERE table_schema = 'public'";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                var column = reader.GetString(1);
                var info = new ColumnInfo(
                    reader.GetString(2),
                    string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase),
                    reader.IsDBNull(4) ? null : reader.GetString(4));

                if (!cache._tables.TryGetValue(table, out var columns))
                {
                    columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
                    cache._tables[table] = columns;
                }

                columns[column] = info;
            }

            return cache;
        }

        public bool TableExists(string table) => _tables.ContainsKey(table);

        public bool TryGetColumns(string table, out IReadOnlyDictionary<string, ColumnInfo> columns)
        {
            if (_tables.TryGetValue(table, out var found))
            {
                columns = found;
                return true;
            }

            columns = new Dictionary<string, ColumnInfo>();
            return false;
        }

        public ColumnInfo? GetColumnInfo(string table, string column)
        {
            return _tables.TryGetValue(table, out var columns) && columns.TryGetValue(column, out var info)
                ? info
                : null;
        }

        public string? GetColumnType(string table, string column) => GetColumnInfo(table, column)?.DataType;

        /// <summary>Учитывает колонку, которую только что добавили или изменили в этой же сессии.</summary>
        public void SetColumn(string table, string column, ColumnInfo info)
        {
            if (!_tables.TryGetValue(table, out var columns))
            {
                columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
                _tables[table] = columns;
            }

            columns[column] = info;
        }

        public void AddColumn(string table, string column, string type) =>
            SetColumn(table, column, new ColumnInfo(type, true, null));
    }
}
