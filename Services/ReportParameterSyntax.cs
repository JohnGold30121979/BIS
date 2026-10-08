using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Единственное место, где описан синтаксис ссылки на параметр отчёта.
    /// В фильтре отчёта значение вида "{PeriodStart}" означает "подставить значение
    /// одноимённого параметра отчёта". Литеральные значения работают как раньше.
    /// Токен должен занимать значение фильтра целиком: "с {A} по {B}" не поддерживается.
    /// </summary>
    public static class ReportParameterSyntax
    {
        private static readonly Regex TokenRegex =
            new(@"\{([A-Za-zА-Яа-яЁё0-9_]+)\}", RegexOptions.Compiled);

        /// <summary>Есть ли в значении фильтра ссылка на параметр.</summary>
        public static bool ContainsToken(string? value) =>
            !string.IsNullOrWhiteSpace(value) && TokenRegex.IsMatch(value);

        /// <summary>
        /// Возвращает имя параметра, если значение целиком является токеном "{Имя}".
        /// Иначе null (в том числе для строки с текстом вокруг токена).
        /// </summary>
        public static string? GetSingleToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var match = TokenRegex.Match(value);
            if (!match.Success)
                return null;

            return match.Index == 0 && match.Length == value.Length
                ? match.Groups[1].Value
                : null;
        }

        /// <summary>Все имена параметров, упомянутые в строке.</summary>
        public static IEnumerable<string> ExtractTokens(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Enumerable.Empty<string>();

            return TokenRegex.Matches(value).Select(match => match.Groups[1].Value);
        }

        /// <summary>Токен для подсказки в UI.</summary>
        public static string Format(string parameterName) => $"{{{parameterName}}}";
    }
}
