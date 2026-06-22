using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace BIS.ERP.Services
{
    public sealed class LocalizationService
    {
        private readonly AppDbContext _context;
        private readonly Dictionary<string, string> _translations = new(StringComparer.OrdinalIgnoreCase);
        public static LocalizationService? Current { get; private set; }
        public string Culture { get; private set; }

        private static readonly Dictionary<string, Dictionary<string, string>> Defaults = new()
        {
            ["ru-RU"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["value.true"] = "Да", ["value.false"] = "Нет",
                ["account.active"] = "Активный", ["account.passive"] = "Пассивный",
                ["account.active_passive"] = "Активно-пассивный",
                ["status.open"] = "Открыт", ["status.collected"] = "Данные собраны",
                ["status.closed"] = "Закрыт", ["status.active"] = "Активен",
                ["status.inactive"] = "Неактивен", ["journal.purchase"] = "Закупки",
                ["journal.sale"] = "Продажи"
            },
            ["en-US"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["value.true"] = "Yes", ["value.false"] = "No",
                ["account.active"] = "Active", ["account.passive"] = "Passive",
                ["account.active_passive"] = "Active-passive",
                ["status.open"] = "Open", ["status.collected"] = "Collected",
                ["status.closed"] = "Closed", ["status.active"] = "Active",
                ["status.inactive"] = "Inactive", ["journal.purchase"] = "Purchases",
                ["journal.sale"] = "Sales"
            }
        };

        public LocalizationService(AppDbContext context, string? culture = null)
        {
            _context = context;
            Culture = string.IsNullOrWhiteSpace(culture) ? "ru-RU" : culture;
        }

        public async Task InitializeAsync()
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS ""LocalizationEntries"" (
                    ""Id"" uuid PRIMARY KEY,
                    ""Culture"" varchar(10) NOT NULL,
                    ""Key"" varchar(200) NOT NULL,
                    ""Value"" text NOT NULL,
                    ""Category"" varchar(50) NOT NULL,
                    ""IsActive"" boolean NOT NULL DEFAULT true,
                    ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LocalizationEntries_Culture_Key""
                    ON ""LocalizationEntries"" (""Culture"", ""Key"");";
            await _context.Database.ExecuteSqlRawAsync(sql);

            foreach (var (culture, translations) in Defaults)
            {
                foreach (var (key, value) in translations)
                {
                    if (!await _context.LocalizationEntries.AnyAsync(entry => entry.Culture == culture && entry.Key == key))
                    {
                        await _context.LocalizationEntries.AddAsync(new LocalizationEntry
                        {
                            Culture = culture, Key = key, Value = value, Category = "System"
                        });
                    }
                }
            }
            await _context.SaveChangesAsync();
            await SetCultureAsync(Culture);
            Current = this;
        }

        public async Task SetCultureAsync(string culture)
        {
            Culture = Defaults.ContainsKey(culture) ? culture : "ru-RU";
            _translations.Clear();
            foreach (var (key, value) in Defaults[Culture])
                _translations[key] = value;

            var databaseValues = await _context.LocalizationEntries.AsNoTracking()
                .Where(entry => entry.Culture == Culture && entry.IsActive)
                .ToListAsync();
            foreach (var entry in databaseValues)
                _translations[entry.Key] = entry.Value;

            if (Application.Current != null)
            {
                foreach (var (key, value) in _translations)
                    Application.Current.Resources[$"Loc.{key}"] = value;
            }
        }

        public string Translate(string key, string? fallback = null)
        {
            return _translations.TryGetValue(key, out var value) ? value : fallback ?? key;
        }

        public string TranslateValue(object? value)
        {
            if (value is bool boolean)
                return Translate(boolean ? "value.true" : "value.false", boolean ? "Да" : "Нет");

            var text = value?.ToString() ?? string.Empty;
            var key = text.Trim().ToLowerInvariant() switch
            {
                "true" => "value.true", "false" => "value.false",
                "active" => "account.active", "passive" => "account.passive",
                "activepassive" or "active-passive" => "account.active_passive",
                "open" => "status.open", "collected" => "status.collected",
                "closed" => "status.closed", "inactive" => "status.inactive",
                "purchase" => "journal.purchase", "sale" => "journal.sale",
                _ => string.Empty
            };
            return string.IsNullOrEmpty(key) ? text : Translate(key, text);
        }

        public static string DisplayValue(object? value)
        {
            if (Current != null)
                return Current.TranslateValue(value);
            return value switch
            {
                true => "Да", false => "Нет", "Active" => "Активный",
                "Passive" => "Пассивный", "ActivePassive" => "Активно-пассивный",
                _ => value?.ToString() ?? string.Empty
            };
        }
    }

    public sealed class LocalizedValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            LocalizationService.DisplayValue(value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
