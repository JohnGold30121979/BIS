using System.ComponentModel;
using System.Text.Json;

namespace BIS.ERP.Models
{
    public interface ILineAnalyticsRow
    {
        LineAnalytics Analytics { get; }
    }

    public sealed class LineAnalyticValue : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private bool _isApplicable;
        public Guid? Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value ?? string.Empty;
                Id = null;
                Code = string.Empty;
                Name = string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsApplicable
        {
            get => _isApplicable;
            set
            {
                if (_isApplicable == value) return;
                _isApplicable = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsApplicable)));
            }
        }
        public void Select(Guid id, string code, string name)
        {
            Text = string.IsNullOrWhiteSpace(name) ? code : $"{code} - {name}";
            Id = id;
            Code = code;
            Name = name;
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>Аналитика именно строки; ключ — код связи из настройки плана счетов.</summary>
    public sealed class LineAnalytics
    {
        // Не содержит скобок, которые ExecuteSqlRaw разбирает как String.Format.
        public const string EmptySqlDefault = "(json_build_object()::text)";
        private Dictionary<string, LineAnalyticValue> _values = new(StringComparer.OrdinalIgnoreCase);
        public LineAnalyticValue this[string code]
        {
            get
            {
                if (!_values.TryGetValue(code, out var value))
                    _values[code] = value = new LineAnalyticValue();
                return value;
            }
        }
        public string ToJson() => JsonSerializer.Serialize(_values);
        public static LineAnalytics FromJson(string? json)
        {
            var result = new LineAnalytics();
            if (!string.IsNullOrWhiteSpace(json))
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, StoredValue>>(json) ?? new();
                foreach (var (code, stored) in values)
                {
                    var value = result[code];
                    value.Text = stored.Text;
                    value.Id = stored.Id;
                    value.Code = stored.Code;
                    value.Name = stored.Name;
                }
            }
            return result;
        }
        private sealed class StoredValue
        {
            public Guid? Id { get; set; }
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
        }
    }
}
