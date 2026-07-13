using DotNetDBF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BIS.ERP.Services
{
    public sealed class PaymentClassificationDbfImportService
    {
        private static readonly IReadOnlyList<PaymentClassificationDbfFieldMap> KnownFieldMappings =
        [
            new() { SourceField = "KODPL", TargetField = "Код", TargetColumn = "code", Description = "Числовой код классификации платежа." },
            new() { SourceField = "NAME_PL", TargetField = "Наименование", TargetColumn = "name", Description = "Наименование классификации платежа." },
            new() { SourceField = "CKODPL", TargetField = "Внешний код", TargetColumn = "external_code", Description = "Строковый код из Fox." }
        ];

        public PaymentClassificationDbfAnalysis Analyze(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Не указан путь к DBF файлу классификации платежей.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл классификации платежей не найден.", filePath);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            using var reader = new DBFReader(filePath);
            reader.CharEncoding = Encoding.GetEncoding(1251);

            var fields = reader.Fields ?? [];
            EnsureRequiredFields(fields);

            var sourceFields = fields
                .Select(field => field.Name?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToList();

            var itemsByCode = new Dictionary<string, PaymentClassificationRecord>(StringComparer.OrdinalIgnoreCase);
            var duplicateCodes = 0;

            object[]? record;
            while ((record = reader.NextRecord()) != null)
            {
                var values = CreateValueMap(fields, record);
                var item = MapPaymentClassification(values);
                if (string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.Name))
                    continue;

                if (itemsByCode.ContainsKey(item.Code))
                    duplicateCodes++;

                itemsByCode[item.Code] = item;
            }

            var ignoredFields = sourceFields
                .Where(field => KnownFieldMappings.All(mapping =>
                    !mapping.SourceField.Equals(field, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new PaymentClassificationDbfAnalysis
            {
                SourcePath = filePath,
                SourceRecordCount = reader.RecordCount,
                LoadedItemsCount = itemsByCode.Count,
                DuplicateSourceCodesCount = duplicateCodes,
                Items = itemsByCode.Values
                    .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                FieldMappings = KnownFieldMappings.ToList(),
                IgnoredSourceFields = ignoredFields
            };
        }

        private static void EnsureRequiredFields(IEnumerable<DBFField> fields)
        {
            var available = fields
                .Select(field => field.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var required = new[] { "KODPL", "NAME_PL" };
            var missing = required.Where(field => !available.Contains(field)).ToList();
            if (missing.Count == 0)
                return;

            throw new InvalidDataException(
                $"В DBF файле отсутствуют обязательные поля: {string.Join(", ", missing)}.");
        }

        private static Dictionary<string, object?> CreateValueMap(IReadOnlyList<DBFField> fields, IReadOnlyList<object?> record)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < fields.Count && index < record.Count; index++)
            {
                var fieldName = fields[index].Name?.Trim();
                if (string.IsNullOrWhiteSpace(fieldName))
                    continue;

                values[fieldName] = record[index];
            }

            return values;
        }

        private static PaymentClassificationRecord MapPaymentClassification(IReadOnlyDictionary<string, object?> values)
        {
            var code = NormalizeCode(GetString(values, "CKODPL"));
            if (string.IsNullOrWhiteSpace(code))
                code = NormalizeCode(GetString(values, "KODPL"));

            return new PaymentClassificationRecord
            {
                Code = code,
                Name = NormalizeText(GetString(values, "NAME_PL")),
                ExternalCode = NormalizeCode(GetString(values, "CKODPL")),
                IsActive = true
            };
        }

        private static string NormalizeCode(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (decimal.TryParse(text, out var decimalValue))
                return decimal.Truncate(decimalValue).ToString("0");

            return text;
        }

        private static string NormalizeText(string? value)
        {
            return string.Join(" ", (value ?? string.Empty)
                .Split([' '], StringSplitOptions.RemoveEmptyEntries));
        }

        private static string GetString(IReadOnlyDictionary<string, object?> values, string fieldName)
        {
            if (!values.TryGetValue(fieldName, out var value) || value == null || value is DBNull)
                return string.Empty;

            return value switch
            {
                DateTime date => date.ToString("dd.MM.yyyy"),
                decimal number => decimal.Truncate(number).ToString("0"),
                double number => Math.Truncate(number).ToString("0"),
                float number => Math.Truncate(number).ToString("0"),
                int number => number.ToString(),
                long number => number.ToString(),
                _ => value.ToString()?.Trim() ?? string.Empty
            };
        }
    }

    public sealed class PaymentClassificationRecord
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string ExternalCode { get; init; } = string.Empty;
        public bool IsActive { get; init; }
    }

    public sealed class PaymentClassificationDbfAnalysis
    {
        public string SourcePath { get; init; } = string.Empty;
        public int SourceRecordCount { get; init; }
        public int LoadedItemsCount { get; init; }
        public int DuplicateSourceCodesCount { get; init; }
        public IReadOnlyList<PaymentClassificationRecord> Items { get; init; } = Array.Empty<PaymentClassificationRecord>();
        public IReadOnlyList<PaymentClassificationDbfFieldMap> FieldMappings { get; init; } = Array.Empty<PaymentClassificationDbfFieldMap>();
        public IReadOnlyList<string> IgnoredSourceFields { get; init; } = Array.Empty<string>();
    }

    public sealed class PaymentClassificationDbfImportResult
    {
        public string SourcePath { get; init; } = string.Empty;
        public int SourceRecordCount { get; init; }
        public int LoadedItemsCount { get; init; }
        public int InsertedCount { get; init; }
        public int UpdatedCount { get; init; }
        public int DeactivatedCount { get; init; }
        public int DuplicateSourceCodesCount { get; init; }
        public IReadOnlyList<PaymentClassificationDbfFieldMap> FieldMappings { get; init; } = Array.Empty<PaymentClassificationDbfFieldMap>();
        public IReadOnlyList<string> IgnoredSourceFields { get; init; } = Array.Empty<string>();
    }

    public sealed class PaymentClassificationDbfFieldMap
    {
        public string SourceField { get; init; } = string.Empty;
        public string TargetField { get; init; } = string.Empty;
        public string TargetColumn { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}
