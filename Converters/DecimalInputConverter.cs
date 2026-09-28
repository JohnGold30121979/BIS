using BIS.ERP.Services;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BIS.ERP.Converters
{
    /// <summary>
    /// Конвертер для редактируемых числовых ячеек таблиц.
    /// Показывает число в формате текущей культуры (например «999,99»), а при вводе
    /// принимает и запятую, и точку как десятичный разделитель. Без конвертера биндинг
    /// культуры ru-RU считал ввод с точкой («999.99» из цифровой клавиатуры)
    /// недопустимым: ячейка оставалась с ошибкой валидации, строка становилась невалидной.
    /// Формат вывода задаётся через ConverterParameter, например N2 или 0.####.
    /// </summary>
    public sealed class DecimalInputConverter : IValueConverter
    {
        private const string DefaultFormat = "N2";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var numberCulture = culture ?? CultureInfo.CurrentCulture;
            var format = parameter as string;
            if (string.IsNullOrWhiteSpace(format))
                format = DefaultFormat;

            return value switch
            {
                null => string.Empty,
                decimal decimalValue => decimalValue.ToString(format, numberCulture),
                double doubleValue => doubleValue.ToString(format, numberCulture),
                float floatValue => floatValue.ToString(format, numberCulture),
                int intValue => intValue.ToString(format, numberCulture),
                long longValue => longValue.ToString(format, numberCulture),
                _ => value.ToString() ?? string.Empty
            };
        }

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
