using Npgsql;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Приведение значений к фактическому типу колонки.
    ///
    /// Нужно там, где строки вставляются/обновляются параметрами: Npgsql отправляет
    /// строку как text, и PostgreSQL отвечает 42804 для integer/uuid/даты.
    /// </summary>
    internal static class DbValueConverter
    {
        public static async Task<Dictionary<string, string>> LoadColumnTypesAsync(
            DbConnection connection, string tableName)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT column_name, data_type
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @table";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result[reader.GetString(0)] = reader.GetString(1);

            return result;
        }

        /// <summary>
        /// Дата из значения любого вида: DateTime приходит из UI напрямую,
        /// строка — из импорта и SQL-настроек.
        /// </summary>
        private static DateTime? ReadDateTime(object? value, string text)
        {
            if (value is DateTime direct)
                return direct;

            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
        }

        public static object Convert(object? value, string? dataType)
        {
            if (value is null or DBNull)
                return DBNull.Value;

            var text = value as string ??
                       System.Convert.ToString(value, CultureInfo.InvariantCulture) ??
                       string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                return DBNull.Value;

            switch ((dataType ?? string.Empty).ToLowerInvariant())
            {
                case "integer":
                case "smallint":
                case "bigint":
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                        ? number
                        : DBNull.Value;

                case "numeric":
                case "real":
                case "double precision":
                    return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec)
                        ? dec
                        : DBNull.Value;

                case "boolean":
                    if (text is "1" or "true" or "True" or "t" or "TRUE") return true;
                    if (text is "0" or "false" or "False" or "f" or "FALSE") return false;
                    return DBNull.Value;

                case "uuid":
                    return Guid.TryParse(text, out var guid) ? guid : DBNull.Value;

                case "timestamp with time zone":
                    // Npgsql принимает в timestamptz только UTC. WPF DatePicker даёт
                    // Kind=Unspecified, DateTime.Now — Kind=Local: оба приводим к UTC,
                    // иначе «Cannot write DateTime with Kind=Local to PostgreSQL type
                    // 'timestamp with time zone'».
                    var utcDate = ReadDateTime(value, text);
                    if (utcDate == null)
                        return DBNull.Value;

                    return utcDate.Value.Kind switch
                    {
                        DateTimeKind.Utc => utcDate.Value,
                        DateTimeKind.Local => utcDate.Value.ToUniversalTime(),
                        _ => DateTime.SpecifyKind(utcDate.Value, DateTimeKind.Utc)
                    };

                case "timestamp without time zone":
                case "date":
                    var naiveDate = ReadDateTime(value, text);
                    return naiveDate == null
                        ? DBNull.Value
                        : DateTime.SpecifyKind(naiveDate.Value, DateTimeKind.Unspecified);

                default:
                    return text;
            }
        }
    }
}
