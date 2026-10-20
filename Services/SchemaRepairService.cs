using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Достраивает отсутствующие колонки в таблицах, которые описывает EF-модель.
    ///
    /// Нужно для старых баз: модель ушла вперёд (ClosedXmlLayout, ShowInAccountingReports
    /// и т.п.), а таблица осталась прежней — EF-запрос падал с 42703 и окно не строилось.
    /// Операция только аддитивная: существующие колонки и данные не трогаются.
    /// </summary>
    public sealed class SchemaRepairService
    {
        private readonly AppDbContext _context;

        public SchemaRepairService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<int> RepairAsync()
        {
            var added = 0;
            var connection = _context.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere)
                await connection.OpenAsync();

            try
            {
                foreach (var entityType in _context.Model.GetEntityTypes())
                {
                    var tableName = entityType.GetTableName();
                    if (string.IsNullOrWhiteSpace(tableName))
                        continue;

                    if (!string.Equals(entityType.GetSchema() ?? "public", "public", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var actualColumns = await LoadColumnsAsync(connection, tableName);
                    if (actualColumns.Count == 0)
                        continue; // таблицы нет — это не наша зона (динамику создаёт MetadataService)

                    foreach (var property in entityType.GetProperties())
                    {
                        var columnName = property.GetColumnName();
                        if (string.IsNullOrWhiteSpace(columnName) || actualColumns.Contains(columnName))
                            continue;

                        var columnType = property.GetColumnType();
                        if (string.IsNullOrWhiteSpace(columnType))
                            continue;

                        var nullable = property.IsNullable;
                        var defaultSql = nullable ? string.Empty : DefaultFor(columnType);
                        var sql =
                            $"ALTER TABLE \"{tableName}\" ADD COLUMN IF NOT EXISTS \"{columnName}\" {columnType}" +
                            (nullable ? string.Empty : " NOT NULL") + defaultSql;

                        try
                        {
                            await _context.Database.ExecuteSqlRawAsync(sql);
                            added++;
                            SystemLogService.Warning(
                                $"Схема: добавлена отсутствующая колонка {tableName}.{columnName} ({columnType}).",
                                "Schema");
                        }
                        catch (Exception ex)
                        {
                            SystemLogService.Warning(
                                $"Схема: не удалось добавить {tableName}.{columnName}: {ex.Message}", "Schema");
                        }
                    }
                }
            }
            finally
            {
                if (openedHere)
                    await connection.CloseAsync();
            }

            return added;
        }

        private static string DefaultFor(string columnType)
        {
            var type = columnType.ToLowerInvariant();

            if (type.Contains("character") || type.Contains("text"))
                return " DEFAULT ''";
            if (type.Contains("numeric") || type.Contains("decimal") || type.Contains("real") || type.Contains("double"))
                return " DEFAULT 0";
            if (type.Contains("boolean"))
                return " DEFAULT false";
            if (type.Contains("timestamp") || type.Contains("date"))
                return " DEFAULT now()";

            return string.Empty;
        }

        private static async Task<HashSet<string>> LoadColumnsAsync(DbConnection connection, string tableName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @table";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(reader.GetString(0));

            return result;
        }
    }
}
