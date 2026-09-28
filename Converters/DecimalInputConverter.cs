using BIS.ERP.Services;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BIS.ERP.Converters
{
    /// <summary>
    /// Единый конвертер числовых ячеек таблиц (редактируемых и только для чтения).
    /// Отображение: разряды разделяются пробелом («1 234 567,89»), дробная часть — запятой.
    /// Формат задаётся через ConverterParameter: N2 (по умолчанию), N3, 0.## и т.п.
    /// При вводе принимаются и запятая, и точка как десятичный разделитель: без конвертера
    /// биндинг культуры считал ввод с точкой («999.99» из цифровой клавиатуры)
    /// недопустимым — ячейка оставалась с ошибкой валидации, строка становилась невалидной.
    /// Строковые значения (detail-гриды, где суммы лежат строками) также приводятся к этому
    /// формату, нечисловые строки выводятся без изменений.
    /// </summary>
    public sealed class DecimalInputConverter : IValueConverter
    {
        private const string DefaultFormat = "N2";

        /// <summary>
        /// Фиксированный формат отображения: разряды — пробелом, дробная часть — запятой.
        /// Независим от региональных настроек ОС, чтобы колонки сумм везде выглядели одинаково.
        /// </summary>
        private static readonly NumberFormatInfo DisplayFormat = CreateDisplayFormat();

        private static NumberFormatInfo CreateDisplayFormat()
        {
            var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            format.NumberGroupSeparator = " ";   // разбивка разрядов — пробелом
            format.NumberGroupSizes = new[] { 3 };
            format.NumberDecimalSeparator = ","; // дробная часть — запятой
            format.NumberDecimalDigits = 2;
            format.NegativeSign = "-";
            return format;
        }

        /// <summary>
        /// Приводит custom-формат («0.##») к формату с группировкой разрядов («#,##0.##»),
        /// чтобы и такие колонки отображались с пробелами между разрядами.
        /// Стандартные N-форматы (N2, N3) группировку уже содержат.
        /// </summary>
        private static string EnsureGrouping(string format) =>
            format.Length > 0 && format[0] == '0' ? "#,##" + format : format;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var format = parameter as string;
            if (string.IsNullOrWhiteSpace(format))
                format = DefaultFormat;
            format = EnsureGrouping(format);

            return value switch
            {
                null => string.Empty,
                DBNull => string.Empty,
                decimal decimalValue => decimalValue.ToString(format, DisplayFormat),
                double doubleValue => doubleValue.ToString(format, DisplayFormat),
                float floatValue => floatValue.ToString(format, DisplayFormat),
                int intValue => intValue.ToString(format, DisplayFormat),
                long longValue => longValue.ToString(format, DisplayFormat),
                // Значения detail-гридов могут прийти строками («1234.56») —
                // разбираем их тем же способом и показываем в едином формате.
                string text => FormatText(text, format),
                _ => value.ToString() ?? string.Empty
            };
        }

        private static string FormatText(string text, string format)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (NumericInputHelper.TryParseDecimal(text, CultureInfo.CurrentCulture, out var parsed))
                return parsed.ToString(format, DisplayFormat);

            return text;
        }

        /// <summary>
        /// Форматирует число в едином стиле отображения сумм (разряды пробелом, дробная часть запятой).
        /// Используется из кода окон для подписей вида «Итого: …».
        /// </summary>
        public static string Format(decimal value, string format = DefaultFormat) =>
            value.ToString(EnsureGrouping(format), DisplayFormat);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal decimalValue)
                return decimalValue;

            if (value is not string text || string.IsNullOrWhiteSpace(text))
                return DependencyProperty.UnsetValue;

            var numberCulture = culture ?? CultureInfo.CurrentCulture;
            return NumericInputHelper.TryParseDecimal(text, numberCulture, out var parsed)
                ? parsed
                : DependencyProperty.UnsetValue;
        }
    }
}
