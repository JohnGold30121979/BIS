using BIS.ERP.Models;
using DotNetDBF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BIS.ERP.Services
{
    public sealed class ChartOfAccountsDbfImportService
    {
        private static readonly IReadOnlyList<ChartOfAccountsDbfFieldMap> KnownFieldMappings =
        [
            new() { SourceField = "SCHET", TargetField = "Код", TargetColumn = "code", Description = "Номер счета из Fox." },
            new() { SourceField = "NAIM", TargetField = "Наименование", TargetColumn = "name", Description = "Основное наименование счета." },
            new() { SourceField = "NAIM_A", TargetField = "Описание", TargetColumn = "description", Description = "Дополнительное описание счета." },
            new() { SourceField = "PRSCH", TargetField = "Тип счета", TargetColumn = "account_type", Description = "Признак активный / пассивный / активно-пассивный." },
            new() { SourceField = "SV_O", TargetField = "Связь с организациями", TargetColumn = "link_organizations", Description = "Флажок аналитики по организациям." },
            new() { SourceField = "SV_T", TargetField = "Связь со списочным составом", TargetColumn = "link_employees", Description = "Флажок аналитики по сотрудникам." },
            new() { SourceField = "SV_M", TargetField = "Связь с материалами", TargetColumn = "link_materials", Description = "Флажок аналитики по материалам." },
            new() { SourceField = "SV_V", TargetField = "Связь с валютами", TargetColumn = "link_currencies", Description = "Флажок аналитики по валютам." },
            new() { SourceField = "SV_L", TargetField = "Связь с лицевыми счетами", TargetColumn = "link_personal_accounts", Description = "Флажок аналитики по лицевым счетам." },
            new() { SourceField = "KOD_ARM", TargetField = "Закрывает модуль", TargetColumn = "closing_module_code", Description = "Код старого модуля FoxPro, который при импорте сопоставляется с модулем BIS ERP." },
            new() { SourceField = "NAL", TargetField = "Код налога", TargetColumn = "tax_code", Description = "Налоговый код счета." },
            new() { SourceField = "PR_O", TargetField = "Признак печати", TargetColumn = "print_mode", Description = "Режим вывода в печатных формах." },
            new() { SourceField = "PR_PR", TargetField = "Сохранять остатки", TargetColumn = "balance_mode", Description = "Режим хранения остатков." },
            new() { SourceField = "KOD_GR", TargetField = "Группа аналитических статей", TargetColumn = "analytic_group", Description = "Код аналитической группы счета." },
            new() { SourceField = "SV_UCH", TargetField = "Связь с участками", TargetColumn = "link_sites", Description = "Флажок аналитики по участкам." }
        ];

        public ChartOfAccountsDbfAnalysis Analyze(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Не указан путь к DBF файлу плана счетов.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл плана счетов не найден.", filePath);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            using var reader = new DBFReader(filePath);
            reader.CharEncoding = Encoding.GetEncoding(866);

            var fields = reader.Fields ?? [];
            EnsureRequiredFields(fields);

            var sourceFields = fields
                .Select(field => field.Name?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToList();

            var accountsByCode = new Dictionary<string, ChartOfAccount>(StringComparer.OrdinalIgnoreCase);
            var duplicateCodes = 0;

            object[]? record;
            while ((record = reader.NextRecord()) != null)
            {
                var values = CreateValueMap(fields, record);
                var account = MapAccount(values);
                if (string.IsNullOrWhiteSpace(account.Code))
                    continue;

                if (accountsByCode.ContainsKey(account.Code))
                    duplicateCodes++;

                accountsByCode[account.Code] = account;
            }

            var ignoredFields = sourceFields
                .Where(field => KnownFieldMappings.All(mapping =>
                    !mapping.SourceField.Equals(field, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ChartOfAccountsDbfAnalysis
            {
                SourcePath = filePath,
                SourceRecordCount = reader.RecordCount,
                LoadedAccountsCount = accountsByCode.Count,
                DuplicateSourceCodesCount = duplicateCodes,
                Accounts = accountsByCode.Values
                    .OrderBy(account => account.Code, StringComparer.OrdinalIgnoreCase)
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

            var required = new[] { "SCHET", "NAIM", "PRSCH" };
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

        private static ChartOfAccount MapAccount(IReadOnlyDictionary<string, object?> values)
        {
            var parentCode = GetString(values, "SSCHET");

            return new ChartOfAccount
            {
                Code = GetString(values, "SCHET"),
                Name = NormalizeText(GetString(values, "NAIM")),
                Description = NormalizeText(GetString(values, "NAIM_A")),
                AccountType = MapAccountType(GetInt(values, "PRSCH")),
                Level = string.IsNullOrWhiteSpace(parentCode) ? 1 : 2,
                IsActive = true,
                FoxAccountSign = GetInt(values, "PRSCH"),
                FoxVatFlag = GetInt(values, "PR_SCH"),
                FoxFixedAssetFlag = GetInt(values, "PR_SC7"),
                UseOrganizations = GetBool(values, "SV_O"),
                UseEmployees = GetBool(values, "SV_T"),
                UseMaterials = GetBool(values, "SV_M"),
                UseCurrencies = GetBool(values, "SV_V"),
                UsePersonalAccounts = GetBool(values, "SV_L"),
                TaxCode = GetInt(values, "NAL"),
                ReportBreakdownCode = GetInt(values, "KODF_RS"),
                ReportFormCode = GetInt(values, "KODF_RB"),
                AnalyticsGroupCode = GetInt(values, "KOD_GR"),
                ClosingSubsystemCode = GetInt(values, "KOD_ARM"),
                PrintModeCode = GetInt(values, "PR_O"),
                BalanceModeCode = GetInt(values, "PR_PR"),
                UseSites = GetBool(values, "SV_UCH"),
                UseJournal = GetBool(values, "SV_J"),
                BalanceLineCode = NormalizeText(GetString(values, "STR_BAL"))
            };
        }

        private static string NormalizeText(string? value)
        {
            return string.Join(" ", (value ?? string.Empty)
                .Split([' '], StringSplitOptions.RemoveEmptyEntries));
        }

        private static string MapAccountType(int sign)
        {
            return sign switch
            {
                2 => "Passive",
                3 => "ActivePassive",
                _ => "Active"
            };
        }

        private static string GetString(IReadOnlyDictionary<string, object?> values, string fieldName)
        {
            if (!values.TryGetValue(fieldName, out var value) || value == null || value is DBNull)
                return string.Empty;

            return value switch
            {
                DateTime date => date.ToString("dd.MM.yyyy"),
                _ => value.ToString()?.Trim() ?? string.Empty
            };
        }

        private static int GetInt(IReadOnlyDictionary<string, object?> values, string fieldName)
        {
            if (!values.TryGetValue(fieldName, out var value) || value == null || value is DBNull)
                return 0;

            return value switch
            {
                int intValue => intValue,
                short shortValue => shortValue,
                long longValue => (int)longValue,
                decimal decimalValue => (int)decimalValue,
                double doubleValue => (int)doubleValue,
                float floatValue => (int)floatValue,
                bool boolValue => boolValue ? 1 : 0,
                _ => int.TryParse(value.ToString()?.Trim(), out var parsed) ? parsed : 0
            };
        }

        private static bool GetBool(IReadOnlyDictionary<string, object?> values, string fieldName)
        {
            if (!values.TryGetValue(fieldName, out var value) || value == null || value is DBNull)
                return false;

            return value switch
            {
                bool boolValue => boolValue,
                decimal decimalValue => decimalValue != 0,
                int intValue => intValue != 0,
                short shortValue => shortValue != 0,
                long longValue => longValue != 0,
                _ => value.ToString()?.Trim().Equals("T", StringComparison.OrdinalIgnoreCase) == true ||
                     value.ToString()?.Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase) == true ||
                     value.ToString()?.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) == true
            };
        }
    }

    public sealed class ChartOfAccountsDbfAnalysis
    {
        public string SourcePath { get; init; } = string.Empty;
        public int SourceRecordCount { get; init; }
        public int LoadedAccountsCount { get; init; }
        public int DuplicateSourceCodesCount { get; init; }
        public IReadOnlyList<ChartOfAccount> Accounts { get; init; } = Array.Empty<ChartOfAccount>();
        public IReadOnlyList<ChartOfAccountsDbfFieldMap> FieldMappings { get; init; } = Array.Empty<ChartOfAccountsDbfFieldMap>();
        public IReadOnlyList<string> IgnoredSourceFields { get; init; } = Array.Empty<string>();
    }

    public sealed class ChartOfAccountsDbfImportResult
    {
        public string SourcePath { get; init; } = string.Empty;
        public int SourceRecordCount { get; init; }
        public int LoadedAccountsCount { get; init; }
        public int InsertedCount { get; init; }
        public int UpdatedCount { get; init; }
        public int DuplicateSourceCodesCount { get; init; }
        public IReadOnlyList<ChartOfAccountsDbfFieldMap> FieldMappings { get; init; } = Array.Empty<ChartOfAccountsDbfFieldMap>();
        public IReadOnlyList<string> IgnoredSourceFields { get; init; } = Array.Empty<string>();
    }

    public sealed class ChartOfAccountsDbfFieldMap
    {
        public string SourceField { get; init; } = string.Empty;
        public string TargetField { get; init; } = string.Empty;
        public string TargetColumn { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}
