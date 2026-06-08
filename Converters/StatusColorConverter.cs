using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BIS.ERP.Converters
{
    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value as string;
            return status switch
            {
                "Активен" => new SolidColorBrush(Colors.Green),
                "Уволен" => new SolidColorBrush(Colors.Red),
                "В отпуске" => new SolidColorBrush(Colors.Orange),
                "Декрет" => new SolidColorBrush(Colors.Purple),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}