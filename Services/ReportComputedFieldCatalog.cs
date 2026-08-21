using BIS.ERP.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace BIS.ERP.Services
{
    public sealed class ReportComputedFieldDefinition
    {
        public string Name { get; init; } = string.Empty;
        public string FieldName { get; init; } = string.Empty;
        public string Type { get; init; } = "String";
        public string Description { get; init; } = string.Empty;
        public string[] SourceAliases { get; init; } = Array.Empty<string>();
    }

    public static class ReportComputedFieldCatalog
    {
        private static readonly IReadOnlyList<ReportComputedFieldDefinition> Fields = new List<ReportComputedFieldDefinition>
        {
            new() { Name = "Номер документа", FieldName = "number", Type = "String", SourceAliases = new[] { "doc_number", "document_number", "number", "dok", "nomdok" } },
            new() { Name = "Дата документа", FieldName = "date", Type = "DateTime", SourceAliases = new[] { "doc_date", "document_date", "report_date", "date", "d_doc", "d_oper" } },
            new() { Name = "Организация", FieldName = "organization", Type = "String", SourceAliases = new[] { "primary_organization", "organization", "organization_name", "contractor", "contractor_name", "name_kod" } },
            new() { Name = "Касса", FieldName = "cash_desk", Type = "String", SourceAliases = new[] { "cash_desk", "cash_desk_name", "cashdesk", "name_kod" } },
            new() { Name = "Дебет", FieldName = "debit_account", Type = "String", SourceAliases = new[] { "debit_account", "debet", "debit", "schet", "sch" } },
            new() { Name = "Кредит", FieldName = "credit_account", Type = "String", SourceAliases = new[] { "credit_account", "credit", "kredit", "kor_sch", "korsch" } },
            new() { Name = "Сумма", FieldName = "amount", Type = "Decimal", SourceAliases = new[] { "amount", "sum", "summa", "debsum", "credsum" } },
            new() { Name = "Сумма прописью", FieldName = "amount_words", Type = "String", SourceAliases = new[] { "amount", "sum", "summa" }, Description = "Пока возвращает числовую сумму строкой; можно расширить отдельным прописью-сервисом." },
            new() { Name = "Основание", FieldName = "basis", Type = "String", SourceAliases = new[] { "basis", "osn", "osnov", "основание" } },
            new() { Name = "Содержание", FieldName = "description", Type = "String", SourceAliases = new[] { "description", "note", "text", "tex", "sod" } },
            new() { Name = "Модуль", FieldName = "module", Type = "String", SourceAliases = new[] { "module", "module_name", "module_code", "prs" } },
            new() { Name = "Номер бланка", FieldName = "blank_number", Type = "String", SourceAliases = new[] { "blank_number", "tax_blank_number", "nom_bl", "ser_bl" } },
            new() { Name = "ИНН организации", FieldName = "organization_tax_number", Type = "String", SourceAliases = new[] { "organization_tax_number", "inn", "tin", "nni" } },
            new() { Name = "Страница", FieldName = "page_number", Type = "Int", SourceAliases = new[] { "page_number", "_pageno" } }
        };

        public static IReadOnlyList<ReportComputedFieldDefinition> GetFields() => Fields;

        public static bool IsComputedField(string? fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return false;

            return Fields.Any(field =>
                EqualsField(field.FieldName, fieldName) ||
                EqualsField(field.Name, fieldName) ||
                field.SourceAliases.Any(alias => EqualsField(alias, fieldName)));
        }

        public static bool IsComputedReportField(ReportField field)
        {
            return IsComputedField(field.FieldName) || IsComputedField(field.DisplayName);
        }

        public static bool TryGetDefinition(string? fieldName, out ReportComputedFieldDefinition definition)
        {
            definition = Fields.FirstOrDefault(field =>
                EqualsField(field.FieldName, fieldName) ||
                EqualsField(field.Name, fieldName) ||
                field.SourceAliases.Any(alias => EqualsField(alias, fieldName)))!;

            return definition != null;
        }

        public static void AddComputedColumns(DataTable table, MetadataObject source, IEnumerable<ReportField> selectedFields)
        {
            var sourceFieldNames = source.Fields
                .SelectMany(field => new[] { field.Name, field.DbColumnName })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var reportField in selectedFields)
            {
                if (sourceFieldNames.Contains(reportField.FieldName) ||
                    !TryGetDefinition(reportField.FieldName, out var definition))
                    continue;

                var columnName = string.IsNullOrWhiteSpace(reportField.DisplayName)
                    ? definition.Name
                    : reportField.DisplayName;

                if (table.Columns.Contains(columnName))
                    continue;

                var columnType = GetColumnType(definition.Type);
                table.Columns.Add(columnName, columnType);

                foreach (DataRow row in table.Rows)
                    row[columnName] = ConvertValue(Evaluate(definition, row), columnType);
            }
        }
        private static object Evaluate(ReportComputedFieldDefinition definition, DataRow row)
        {
            foreach (var alias in definition.SourceAliases)
            {
                if (!TryGetRowValue(row, alias, out var value))
                    continue;

                if (definition.FieldName == "amount_words")
                    return FormatDecimal(value);

                return value;
            }

            return definition.Type switch
            {
                "Decimal" => 0m,
                "Int" or "Integer" => 0,
                "DateTime" => DBNull.Value,
                _ => string.Empty
            };
        }

        private static bool TryGetRowValue(DataRow row, string columnName, out object value)
        {
            value = DBNull.Value;
            var column = row.Table.Columns
                .Cast<DataColumn>()
                .FirstOrDefault(item => string.Equals(item.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));

            if (column == null)
                return false;

            value = row[column];
            return value != null && value != DBNull.Value && !string.IsNullOrWhiteSpace(value.ToString());
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null || value == DBNull.Value)
                return targetType == typeof(DateTime) ? DBNull.Value : GetDefaultValue(targetType);

            if (targetType == typeof(decimal))
                return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var decimalValue)
                    ? decimalValue
                    : 0m;

            if (targetType == typeof(int))
                return int.TryParse(value.ToString(), out var intValue) ? intValue : 0;

            if (targetType == typeof(DateTime))
                return DateTime.TryParse(value.ToString(), out var dateValue) ? dateValue : DBNull.Value;

            return value.ToString() ?? string.Empty;
        }

        private static Type GetColumnType(string type) => type switch
        {
            "Decimal" => typeof(decimal),
            "Int" or "Integer" => typeof(int),
            "DateTime" => typeof(DateTime),
            _ => typeof(string)
        };

        private static object GetDefaultValue(Type type)
        {
            if (type == typeof(decimal))
                return 0m;
            if (type == typeof(int))
                return 0;
            return string.Empty;
        }

        private static string FormatDecimal(object value)
        {
            return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var decimalValue)
                ? decimalValue.ToString("N2", CultureInfo.CurrentCulture)
                : string.Empty;
        }

        private static bool EqualsField(string? left, string? right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string? value)
        {
            var normalized = (value ?? string.Empty)
                .Trim()
                .Trim('"', '\'', ' ', '=', '{', '}')
                .Replace("!", ".")
                .ToLowerInvariant();
            var separatorIndex = normalized.LastIndexOf('.');
            return separatorIndex >= 0 && separatorIndex < normalized.Length - 1
                ? normalized[(separatorIndex + 1)..]
                : normalized;
        }

    }
}


