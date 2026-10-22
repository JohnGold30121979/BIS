using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Достраивает схему динамических таблиц (catalog_*, doc_*) по метаданным.
    ///
    /// Клиентские базы отстают от конфигурации: в метаданных поле уже есть,
    /// а колонки в таблице нет — тогда падают отчёты и наборы данных
    /// (например, "столбец o.bank_name не существует"). Лечим автоматически
    /// при открытии базы, чтобы ни одну базу не править руками.
    ///
    /// Операция аддитивная и идемпотентная: только ADD COLUMN IF NOT EXISTS.
    /// </summary>
    public sealed class MetadataSchemaRepairService
    {
        private readonly AppDbContext _context;
        private DatabaseSchemaCache? _schema;

        public MetadataSchemaRepairService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<(int Tables, int Columns)> RepairAsync()
        {
            var tablesChecked = 0;
            var columnsAdded = 0;

            var objects = await _context.Set<MetadataObject>()
                .Include(item => item.Fields)
                .AsNoTracking()
                .ToListAsync();

            foreach (var metadataObject in objects)
            {
                var tableName = metadataObject.TableName;
                if (string.IsNullOrWhiteSpace(tableName) || metadataObject.Fields.Count == 0)
                    continue;

                var actualColumns = await LoadColumnsAsync(tableName);
                if (actualColumns.Count == 0)
                    continue; // таблицы ещё нет — её создаст MetadataService по метаданным

                tablesChecked++;

                foreach (var field in metadataObject.Fields)
                {
                    var columnName = field.DbColumnName;
                    if (string.IsNullOrWhiteSpace(columnName) || actualColumns.Contains(columnName))
                        continue;

                    var sqlType = GetSqlType(field);

                    // Ссылочные поля не делаем обязательными: придуманный uuid нарушит FK.
                    var notNull = field.IsRequired && !string.Equals(field.FieldType, "Reference", StringComparison.OrdinalIgnoreCase);
                    var sql = new StringBuilder()
                        .Append("ALTER TABLE \"").Append(tableName).Append("\" ADD COLUMN IF NOT EXISTS \"")
                        .Append(columnName).Append("\" ").Append(sqlType);

                    if (notNull)
                        sql.Append(" NOT NULL").Append(DefaultFor(sqlType));

                    try
                    {
                        await _context.Database.ExecuteSqlRawAsync(sql.ToString());
                        _schema?.AddColumn(tableName, columnName, sqlType);
                        columnsAdded++;
                        SystemLogService.Warning(
                            $"Схема: в {tableName} добавлена колонка {columnName} ({sqlType}) по метаданным.",
                            "Schema");
                    }
                    catch (Exception ex)
                    {
                        SystemLogService.Warning(
                            $"Схема: не удалось добавить {tableName}.{columnName}: {ex.Message}", "Schema");
                    }
                }
            }

            return (tablesChecked, columnsAdded);
        }

        private static string GetSqlType(MetadataField field)
        {
            return field.FieldType switch
            {
                "String" => $"VARCHAR({(field.Length > 0 ? field.Length : 255)})",
                "Int" => "INTEGER",
                "Decimal" => $"DECIMAL({(field.Precision > 0 ? field.Precision : 18)}, {(field.Scale > 0 ? field.Scale : 2)})",
                "DateTime" => "TIMESTAMP",
                "Bool" => "BOOLEAN",
                "Reference" => "uuid",
                _ => "TEXT"
            };
        }

        private static string DefaultFor(string sqlType)
        {
            var type = sqlType.ToUpperInvariant();

            if (type.StartsWith("VARCHAR") || type == "TEXT")
                return " DEFAULT ''";
            if (type.StartsWith("DECIMAL") || type == "INTEGER" || type == "BIGINT")
                return " DEFAULT 0";
            if (type == "BOOLEAN")
                return " DEFAULT false";
            if (type.StartsWith("TIMESTAMP") || type == "DATE")
                return " DEFAULT now()";

            return string.Empty;
        }

        private async Task<HashSet<string>> LoadColumnsAsync(string tableName)
        {
            _schema ??= await DatabaseSchemaCache.LoadAsync(
                _context.Database.GetConnectionString() ?? throw new InvalidOperationException("Не задана строка подключения."));

            return _schema.TryGetColumns(tableName, out var columns)
                ? new HashSet<string>(columns.Keys, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
