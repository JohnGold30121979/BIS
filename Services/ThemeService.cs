using System;
using System.Windows;
using System.Windows.Media;

namespace BIS.ERP.Services
{
    public static class ThemeService
    {
        public const string DefaultTheme = "Default";
        public const string DarkTheme = "Dark";

        public static void Apply(string theme)
        {
            var normalizedTheme = string.Equals(theme, DarkTheme, StringComparison.OrdinalIgnoreCase)
                ? DarkTheme
                : DefaultTheme;

            var resources = Application.Current.Resources;

            if (normalizedTheme == DarkTheme)
            {
                resources["AppWindowBackgroundBrush"] = Brush("#1F2933");
                resources["AppPanelBackgroundBrush"] = Brush("#2C3E50");
                resources["AppCardBackgroundBrush"] = Brush("#34495E");
                resources["AppPrimaryTextBrush"] = Brushes.White;
                resources["AppSecondaryTextBrush"] = Brush("#BDC3C7");
                resources["AppInputBackgroundBrush"] = Brush("#F8F9FA");
                resources["AppInputForegroundBrush"] = Brush("#1F2933");
                return;
            }

            resources["AppWindowBackgroundBrush"] = Brushes.White;
            resources["AppPanelBackgroundBrush"] = Brush("#2C3E50");
            resources["AppCardBackgroundBrush"] = Brushes.White;
            resources["AppPrimaryTextBrush"] = Brush("#2C3E50");
            resources["AppSecondaryTextBrush"] = Brush("#666666");
            resources["AppInputBackgroundBrush"] = Brushes.White;
            resources["AppInputForegroundBrush"] = Brush("#333333");
        }

        private static SolidColorBrush Brush(string color)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(color);
        }
    }
}
