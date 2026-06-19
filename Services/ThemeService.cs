using System;
using System.Collections.Generic;
using System.Windows;

namespace BIS.ERP.Services
{
    public static class ThemeService
    {
        public const string DefaultTheme = "Default";
        public const string DarkTheme = "Dark";

        private static readonly Dictionary<string, Uri> ThemeUris = new()
        {
            { DefaultTheme, new Uri("/Themes/Default.xaml", UriKind.Relative) },
            { DarkTheme, new Uri("/Themes/Dark.xaml", UriKind.Relative) }
        };

        private static string _currentTheme = DefaultTheme;

        public static void Apply(string themeName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(themeName) || !ThemeUris.ContainsKey(themeName))
                {
                    themeName = DefaultTheme;
                }

                _currentTheme = themeName;
                var uri = ThemeUris[themeName];

                // Загружаем новую тему
                var newTheme = new ResourceDictionary { Source = uri };

                // ✅ Очищаем и применяем новую тему
                var app = Application.Current;
                if (app != null)
                {
                    app.Resources.MergedDictionaries.Clear();
                    app.Resources.MergedDictionaries.Add(newTheme);
                }

                System.Diagnostics.Debug.WriteLine($"✅ Применена тема: {themeName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка применения темы: {ex.Message}");

                // Fallback: пробуем загрузить Default
                try
                {
                    var defaultUri = new Uri("/Themes/Default.xaml", UriKind.Relative);
                    var defaultTheme = new ResourceDictionary { Source = defaultUri };
                    Application.Current.Resources.MergedDictionaries.Clear();
                    Application.Current.Resources.MergedDictionaries.Add(defaultTheme);
                }
                catch
                {
                    // Игнорируем
                }
            }
        }

        public static string GetCurrentTheme() => _currentTheme;
    }
}