using System;
using System.Collections.Generic;
using System.Linq;

namespace BIS.ERP.Services
{
    public sealed record ChartOfAccountsChoiceDefinition(string Code, string Display, params string[] Aliases)
    {
        public bool Matches(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.IsNullOrWhiteSpace(Code);

            return Code.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                   Display.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                   Aliases.Any(alias => alias.Equals(value, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class ChartOfAccountsSelectionMetadata
    {
        private const string FinanceModuleKey = "finance";
        private const string InventoryModuleKey = "inventory";
        private const string FixedAssetsModuleKey = "fixed_assets";
        private const string SalesModuleKey = "sales";
        private const string AuxiliaryProductionModuleKey = "auxiliary_production";
        private const string MenuModuleKey = "menu";

        public static IReadOnlyList<ChartOfAccountsChoiceDefinition> PrintModeOptions { get; } =
            CreateModeOptions();

        public static IReadOnlyList<ChartOfAccountsChoiceDefinition> BalanceModeOptions { get; } =
            CreateModeOptions();

        public static string NormalizePrintModeValue(string? rawValue) =>
            NormalizeChoiceValue(rawValue, PrintModeOptions);

        public static string NormalizeBalanceModeValue(string? rawValue) =>
            NormalizeChoiceValue(rawValue, BalanceModeOptions);

        public static IReadOnlyList<ChartOfAccountsChoiceDefinition>? GetOptionsForField(string? fieldName)
        {
            return fieldName switch
            {
                "Признак печати" => PrintModeOptions,
                "Сохранять остатки" => BalanceModeOptions,
                _ => null
            };
        }

        public static string GetPrintModeDisplay(string? rawValue) =>
            GetChoiceDisplay(rawValue, PrintModeOptions);

        public static string GetBalanceModeDisplay(string? rawValue) =>
            GetChoiceDisplay(rawValue, BalanceModeOptions);

        public static string NormalizeModeValue(string? fieldName, string? rawValue)
        {
            return fieldName switch
            {
                "Признак печати" => NormalizePrintModeValue(rawValue),
                "Сохранять остатки" => NormalizeBalanceModeValue(rawValue),
                _ => rawValue?.Trim() ?? string.Empty
            };
        }

        public static string GetModeDisplay(string? fieldName, string? rawValue)
        {
            return fieldName switch
            {
                "Признак печати" => GetPrintModeDisplay(rawValue),
                "Сохранять остатки" => GetBalanceModeDisplay(rawValue),
                _ => rawValue?.Trim() ?? string.Empty
            };
        }

        public static IReadOnlyList<Dictionary<string, object>> FilterAccountRowsByModule(
            IEnumerable<Dictionary<string, object>> rows,
            string? moduleCodeOrName)
        {
            var requestedModuleKey = NormalizeModuleKey(moduleCodeOrName);
            if (string.IsNullOrWhiteSpace(requestedModuleKey))
                return rows.ToList();

            return rows
                .Where(row => IsAccountAllowedForModule(
                    row.GetValueOrDefault("Закрывает модуль")?.ToString() ??
                    row.GetValueOrDefault("closing_module_code")?.ToString(),
                    requestedModuleKey))
                .ToList();
        }

        public static bool IsAccountAllowedForModule(string? accountModuleValue, string? requestedModuleCodeOrName)
        {
            var requestedModuleKey = NormalizeModuleKey(requestedModuleCodeOrName);
            if (string.IsNullOrWhiteSpace(requestedModuleKey))
                return true;

            var accountModuleKey = NormalizeModuleKey(accountModuleValue);
            return string.IsNullOrWhiteSpace(accountModuleKey) || accountModuleKey == requestedModuleKey;
        }

        public static string NormalizeModuleDisplayName(string? rawValue)
        {
            var normalizedKey = NormalizeModuleKey(rawValue);
            if (string.IsNullOrWhiteSpace(normalizedKey))
                return string.Empty;

            return normalizedKey switch
            {
                FinanceModuleKey => "Финансы",
                InventoryModuleKey => "Учет материальных ценностей",
                FixedAssetsModuleKey => "Основные средства",
                SalesModuleKey => "Сбыт",
                AuxiliaryProductionModuleKey => "Вспомогательное производство",
                MenuModuleKey => "Меню",
                _ => rawValue?.Trim() ?? string.Empty
            };
        }

        private static IReadOnlyList<ChartOfAccountsChoiceDefinition> CreateModeOptions()
        {
            return
            [
                new ChartOfAccountsChoiceDefinition(string.Empty, string.Empty, "0"),
                new ChartOfAccountsChoiceDefinition("1", "по статьям"),
                new ChartOfAccountsChoiceDefinition("2", "по организациям"),
                new ChartOfAccountsChoiceDefinition("3", "по таб.номерам", "по табельным номерам"),
                new ChartOfAccountsChoiceDefinition("4", "по лиц.счетам", "по лицевым счетам"),
                new ChartOfAccountsChoiceDefinition("5", "по материалам"),
                new ChartOfAccountsChoiceDefinition("6", "по субсчетам")
            ];
        }

        private static string NormalizeChoiceValue(
            string? rawValue,
            IReadOnlyList<ChartOfAccountsChoiceDefinition> options)
        {
            var value = rawValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value == "0")
                return string.Empty;

            var match = options.FirstOrDefault(option => option.Matches(value));
            return match?.Code ?? value;
        }

        private static string GetChoiceDisplay(
            string? rawValue,
            IReadOnlyList<ChartOfAccountsChoiceDefinition> options)
        {
            var normalized = NormalizeChoiceValue(rawValue, options);
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            var match = options.FirstOrDefault(option => option.Code == normalized);
            return match?.Display ?? normalized;
        }

        private static string NormalizeModuleKey(string? rawValue)
        {
            var value = rawValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value == "0")
                return string.Empty;

            return value switch
            {
                "3" or "Finance" or "Финансы" => FinanceModuleKey,
                "4" or "Сбыт" or "Продажи" or "Реализация" or "Регистратура" => SalesModuleKey,
                "6" or "12" or "Inventory" or "Учет материальных ценностей" or "УМЦ" or "ТМЦ" or "Материалы" or "Сырье" => InventoryModuleKey,
                "7" or "FixedAssets" or "Основные средства" or "ОС" => FixedAssetsModuleKey,
                "8" or "Вспомогательное производство" => AuxiliaryProductionModuleKey,
                "66" or "Меню" => MenuModuleKey,
                _ => value.ToLowerInvariant()
            };
        }
    }
}
