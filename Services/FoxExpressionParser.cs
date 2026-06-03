using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;

namespace BIS.ERP.Services
{
    public class FoxExpressionParser
    {
        private readonly Dictionary<string, object> _variables;

        public FoxExpressionParser()
        {
            _variables = new Dictionary<string, object>();
        }

        public void SetVariable(string name, object value)
        {
            _variables[name.ToLower()] = value;
        }

        public object Evaluate(string expression, DataRow row = null)
        {
            if (string.IsNullOrEmpty(expression)) return string.Empty;

            var result = expression;

            // ALLTRIM() - удаляет пробелы
            result = Regex.Replace(result, @"ALLTRIM\(([^)]+)\)", m =>
                (m.Groups[1].Value?.ToString() ?? "").Trim());

            // STR(число, длина, точность)
            result = Regex.Replace(result, @"STR\(([^,]+),(\d+),(\d+)\)", m =>
            {
                if (double.TryParse(m.Groups[1].Value, out double num))
                {
                    return num.ToString($"F{int.Parse(m.Groups[3].Value)}");
                }
                return m.Groups[1].Value;
            });

            // DAY(date)
            result = Regex.Replace(result, @"DAY\(([^)]+)\)", m =>
            {
                if (DateTime.TryParse(m.Groups[1].Value, out DateTime date))
                    return date.Day.ToString();
                return "0";
            });

            // MONTH(date)
            result = Regex.Replace(result, @"MONTH\(([^)]+)\)", m =>
            {
                if (DateTime.TryParse(m.Groups[1].Value, out DateTime date))
                    return date.Month.ToString();
                return "0";
            });

            // YEAR(date)
            result = Regex.Replace(result, @"YEAR\(([^)]+)\)", m =>
            {
                if (DateTime.TryParse(m.Groups[1].Value, out DateTime date))
                    return date.Year.ToString();
                return "0";
            });

            // LEFT(строка, длина)
            result = Regex.Replace(result, @"LEFT\(([^,]+),(\d+)\)", m =>
            {
                var str = m.Groups[1].Value;
                var len = int.Parse(m.Groups[2].Value);
                return str.Length > len ? str.Substring(0, len) : str;
            });

            // RIGHT(строка, длина)
            result = Regex.Replace(result, @"RIGHT\(([^,]+),(\d+)\)", m =>
            {
                var str = m.Groups[1].Value;
                var len = int.Parse(m.Groups[2].Value);
                return str.Length > len ? str.Substring(str.Length - len) : str;
            });

            // SUBSTR(строка, начало, длина)
            result = Regex.Replace(result, @"SUBSTR\(([^,]+),(\d+),(\d+)\)", m =>
            {
                var str = m.Groups[1].Value;
                var start = int.Parse(m.Groups[2].Value) - 1;
                var len = int.Parse(m.Groups[3].Value);
                return start >= 0 && start + len <= str.Length ? str.Substring(start, len) : str;
            });

            // TRIM()
            result = result.Trim();

            // Замена переменных
            foreach (var var in _variables)
            {
                result = result.Replace(var.Key, var.Value?.ToString() ?? "");
            }

            if (row != null && row.Table != null)
            {
                foreach (DataColumn col in row.Table.Columns)
                {
                    result = result.Replace(col.ColumnName.ToLower(), row[col]?.ToString() ?? "");
                    result = result.Replace(col.ColumnName, row[col]?.ToString() ?? "");
                }
            }

            return result;
        }

        public decimal Sum(IEnumerable<DataRow> rows, string fieldName)
        {
            decimal total = 0;
            foreach (var row in rows)
            {
                if (decimal.TryParse(row[fieldName]?.ToString(), out decimal val))
                    total += val;
            }
            return total;
        }

        public int Count(IEnumerable<DataRow> rows)
        {
            int count = 0;
            foreach (var _ in rows) count++;
            return count;
        }

        public decimal Avg(IEnumerable<DataRow> rows, string fieldName)
        {
            var total = Sum(rows, fieldName);
            var count = Count(rows);
            return count > 0 ? total / count : 0;
        }
    }
}