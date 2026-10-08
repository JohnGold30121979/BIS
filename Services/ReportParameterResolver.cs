using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Приведение значений параметров отчёта к типам и подстановка значений по умолчанию.
    /// Значения по умолчанию — либо литерал ("01.01.2025"), либо одно из ключевых слов.
    /// </summary>
    public static class ReportParameterResolver
    {
        public static readonly IReadOnlyDictionary<string, string> DefaultValueKeywords =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["сегодня"] = "сегодня",
                ["начала месяца"] = "начала месяца",
                ["конца месяца"] = "конца месяца",
                ["начала года"] = "начала года",
                ["конца года"] = "конца года",
                ["пусто"] = "пусто"
            };

        /// <summary>
        /// Имена, которые уже понимает ReportDataSetService.AddSqlParameters.
        /// Если назвать параметры так, они будут подставлены и в SQL-наборы данных.
        /// </summary>
        public static readonly IReadOnlyList<string> SeedNames = new[]
        {
            "PeriodStart", "PeriodEnd", "CashDeskId", "OrganizationId", "EmployeeId", "AccountCode"
        };

        /// <summary>Черновик параметров, восстановленный из токенов в фильтрах отчёта.</summary>
        public static List<ReportParameter> BuildSeedFromFilters(IEnumerable<ReportFilter> filters)
        {
            var result = new List<ReportParameter>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var filter in filters.OrderBy(item => item.Order))
            {
                foreach (var token in ReportParameterSyntax.ExtractTokens(filter.Value)
                             .Concat(ReportParameterSyntax.ExtractTokens(filter.Value2)))
                {
                    if (!seen.Add(token))
                        continue;

                    var isDateField = LooksLikeDateRangeName(token);
                    result.Add(new ReportParameter
                    {
                        Name = token,
                        Title = token,
                        ParameterType = isDateField ? ReportParameterTypes.Date : ReportParameterTypes.String,
                        DefaultValue = SuggestDefaultValue(token, isDateField),
                        IsRequired = true,
                        Order = result.Count + 1
                    });
                }
            }

            return result;
        }

        private static string SuggestDefaultValue(string name, bool isDateField)
        {
            if (name.Equals("PeriodStart", StringComparison.OrdinalIgnoreCase))
                return "начала месяца";
            if (name.Equals("PeriodEnd", StringComparison.OrdinalIgnoreCase))
                return "сегодня";

            if (!isDateField)
                return string.Empty;

            return "сегодня";
        }

        private static bool LooksLikeDateRangeName(string name) =>
            name.Contains("period", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("date", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("дата", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("период", StringComparison.OrdinalIgnoreCase);

        /// <summary>Значения по умолчанию для всех параметров отчёта.</summary>
        public static Dictionary<string, object> BuildDefaults(IEnumerable<ReportParameter> parameters)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameters)
            {
                if (string.IsNullOrWhiteSpace(parameter.Name))
                    continue;

                result[parameter.Name] = ResolveDefaultValue(
                    parameter.DefaultValue,
                    string.IsNullOrWhiteSpace(parameter.ParameterType)
                        ? ReportParameterTypes.String
                        : parameter.ParameterType);
            }

            return result;
        }

        /// <summary>
        /// Значения для предпросмотра: значения по умолчанию, поверх которых наложены
        /// сохранённые значения пользователя. Так предпросмотр совпадает с тем, что увидит пользователь.
        /// </summary>
        public static Dictionary<string, object> ResolvePreviewValues(
            Report report,
            IReadOnlyDictionary<string, string>? savedUserValues = null)
        {
            var defaults = BuildDefaults(report.Parameters);
            if (savedUserValues == null)
                return defaults;

            var byName = report.Parameters
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
                .ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var pair in savedUserValues)
            {
                if (!byName.TryGetValue(pair.Key, out var definition) ||
                    string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                defaults[definition.Name] = Coerce(pair.Value, definition.ParameterType);
            }

            return defaults;
        }

        public static object ResolveDefaultValue(string? defaultValue, string parameterType)
        {
            var today = DateTime.Today;
            var keyword = (defaultValue ?? string.Empty).Trim().ToLowerInvariant();

            switch (keyword)
            {
                case "сегодня":
                    return today;
                case "начала месяца":
                    return new DateTime(today.Year, today.Month, 1);
                case "конца месяца":
                    return new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1);
                case "начала года":
                    return new DateTime(today.Year, 1, 1);
                case "конца года":
                    return new DateTime(today.Year, 12, 31);
                case "пусто":
                    return EmptyValue(parameterType);
                default:
                    return Coerce(defaultValue, parameterType);
            }
        }

        /// <summary>Приведение текстового значения к типу параметра. Пустое значение — значение по умолчанию типа.</summary>
        public static object Coerce(string? value, string? parameterType)
        {
            var text = value?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return EmptyValue(parameterType);

            return parameterType switch
            {
                ReportParameterTypes.Date
                    => DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date)
                        ? date.Date
                        : DateTime.Today,

                ReportParameterTypes.Int
                    => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)
                        ? intValue
                        : 0,

                ReportParameterTypes.Decimal
                    => decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue)
                        ? decimalValue
                        : 0m,

                ReportParameterTypes.Bool
                    => text.Equals("true", StringComparison.OrdinalIgnoreCase)
                       || text.Equals("1", StringComparison.Ordinal)
                       || text.Equals("да", StringComparison.OrdinalIgnoreCase)
                       || text.Equals("истина", StringComparison.OrdinalIgnoreCase),

                ReportParameterTypes.Reference => text,

                _ => text
            };
        }

        /// <summary>Пустое значение для типа: для необязательных условий это признак «условие не задано».</summary>
        public static bool IsEmptyValue(object? value, string? parameterType)
        {
            if (value == null || value == DBNull.Value)
                return true;

            if (string.IsNullOrWhiteSpace(value.ToString()))
                return true;

            return parameterType switch
            {
                ReportParameterTypes.Date => value is DateTime date && date == default,
                ReportParameterTypes.Int => value is int intValue && intValue == 0,
                ReportParameterTypes.Decimal => value is decimal decimalValue && decimalValue == 0m,
                _ => false
            };
        }

        private static object EmptyValue(string? parameterType) => parameterType switch
        {
            ReportParameterTypes.Date => DateTime.Today,
            ReportParameterTypes.Int => 0,
            ReportParameterTypes.Decimal => 0m,
            ReportParameterTypes.Bool => false,
            _ => string.Empty
        };

        /// <summary>Текст для поля ввода в UI. Формат даты совпадает с маской DD/MM/YYYY.</summary>
        public static string ToEditableText(object? value) => value switch
        {
            null => string.Empty,
            DateTime date => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            bool flag => flag ? "да" : "нет",
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        /// <summary>Текст для сохранения в настройках пользователя (обратимый к Coerce).</summary>
        public static string ToStorageText(object? value) => value switch
        {
            null => string.Empty,
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
