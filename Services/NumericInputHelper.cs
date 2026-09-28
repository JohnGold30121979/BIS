using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Разбор чисел, которые пользователь вводит вручную в поля и ячейки таблиц.
    /// Цифровая клавиатура может выдать точку как десятичный разделитель, хотя текущая
    /// культура (ru-RU) ожидает запятую. Поэтому принимаются оба разделителя, и ввод
    /// не зависит от региональных настроек рабочего места.
    /// </summary>
    public static class NumericInputHelper
    {
        /// <summary>Разделители, которые считаются «чужими» для текущей культуры.</summary>
        private static readonly string[] FractionSeparators = { ",", "." };

        /// <summary>
        /// Пытается разобрать число: сначала по правилам переданной культуры, затем по
        /// инвариантным правилам (точка как десятичный разделитель).
        /// </summary>
        public static bool TryParseDecimal(string? text, CultureInfo? culture, out decimal value)
        {
            var parsingCulture = culture ?? CultureInfo.CurrentCulture;
            if (TryParseWithCulture(text, parsingCulture, out value))
                return true;

            if (!string.Equals(parsingCulture.Name, CultureInfo.InvariantCulture.Name, StringComparison.Ordinal))
                return TryParseWithCulture(text, CultureInfo.InvariantCulture, out value);

            value = 0m;
            return false;
        }

        private static bool TryParseWithCulture(string? text, CultureInfo culture, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var candidate in BuildCandidates(text!, culture))
            {
                if (decimal.TryParse(candidate, NumberStyles.Any, culture, out value))
                    return true;
            }

            value = 0m;
            return false;
        }

        /// <summary>
        /// Готовит варианты написания числа, чтобы и «999,99», и «999.99» читались как
        /// одна и та же дробь вне зависимости от культуры.
        /// </summary>
        private static List<string> BuildCandidates(string text, CultureInfo culture)
        {
            var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
            var foreignSeparators = FractionSeparators
                .Where(separator => !string.Equals(separator, decimalSeparator, StringComparison.Ordinal))
                .ToArray();

            var trimmed = text.Trim();
            var compact = trimmed.Replace(" ", string.Empty).Replace("\u00A0", string.Empty);
            var sources = string.Equals(trimmed, compact, StringComparison.Ordinal)
                ? new[] { trimmed }
                : new[] { trimmed, compact };

            var candidates = new List<string>();
            foreach (var source in sources)
            {
                var cultureCount = CountOccurrences(source, decimalSeparator);
                var foreignCount = foreignSeparators.Sum(separator => CountOccurrences(source, separator));

                // Единственный разделитель (точка или запятая) — это десятичный разделитель:
                // именно так вводят дробные числа с цифровой клавиатуры.
                if (cultureCount + foreignCount == 1)
                    candidates.Add(ReplaceForeignSeparators(source, FractionSeparators, decimalSeparator));

                candidates.Add(source);

                if (cultureCount > 0 && foreignCount > 0)
                {
                    // Оба разделителя сразу: десятичный — самый правый, остальные отбрасываем.
                    candidates.Add(NormalizeSeparators(source, decimalSeparator, foreignSeparators));
                    continue;
                }

                // Несколько «чужих» разделителей подряд — это разделители групп разрядов.
                if (foreignCount > 1)
                    candidates.Add(RemoveOccurrences(source, foreignSeparators));
            }

            return candidates;
        }

        private static string ReplaceForeignSeparators(
            string text,
            IReadOnlyList<string> foreignSeparators,
            string decimalSeparator)
        {
            var result = text;
            foreach (var separator in foreignSeparators)
                result = result.Replace(separator, decimalSeparator);

            return result;
        }

        private static string RemoveOccurrences(string text, IReadOnlyList<string> separators)
        {
            var result = text;
            foreach (var separator in separators)
                result = result.Replace(separator, string.Empty);

            return result;
        }

        /// <summary>
        /// Оставляет один десятичный разделитель (самый правый из введённых) и убирает
        /// остальные разделители, которые пользователь набрал как разделители групп.
        /// </summary>
        private static string NormalizeSeparators(
            string text,
            string decimalSeparator,
            IReadOnlyList<string> foreignSeparators)
        {
            if (decimalSeparator.Length != 1)
                return text;

            var foreignChars = foreignSeparators
                .Where(separator => separator.Length == 1)
                .Select(separator => separator[0])
                .ToArray();
            if (foreignChars.Length == 0)
                return text;

            var decimalChar = decimalSeparator[0];
            var decimalIndex = text.LastIndexOf(decimalChar);
            foreach (var foreignChar in foreignChars)
                decimalIndex = Math.Max(decimalIndex, text.LastIndexOf(foreignChar));

            var builder = new StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                var isSeparator = current == decimalChar || Array.IndexOf(foreignChars, current) >= 0;
                if (!isSeparator)
                {
                    builder.Append(current);
                    continue;
                }

                if (index == decimalIndex)
                    builder.Append(decimalChar);
            }

            return builder.ToString();
        }

        private static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            var count = 0;
            var index = text.IndexOf(value, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
            }

            return count;
        }
    }
}
