using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using BIS.ERP.Services;

namespace BIS.ERP.Converters
{
    public class GuidToNameConverter : IValueConverter
    {
        public static Dictionary<string, Dictionary<Guid, string>> Cache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString()))
                return "";

            if (Guid.TryParse(value.ToString(), out var guid))
            {
                var catalogName = parameter?.ToString();
                if (!string.IsNullOrEmpty(catalogName))
                {
                    if (Cache.TryGetValue(catalogName, out var dict) && dict.TryGetValue(guid, out var name))
                        return name;
                }
                return guid.ToString().Substring(0, 8) + "...";
            }
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}