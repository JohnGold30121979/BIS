using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        private static bool _isInitialized;

        public static void Initialize()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnThemedElementLoaded),
                true);
            EventManager.RegisterClassHandler(
                typeof(UserControl),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnThemedElementLoaded),
                true);
        }

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

                    foreach (Window window in app.Windows)
                        ApplyToVisualTree(window);
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

        private static void OnThemedElementLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is DependencyObject root)
                ApplyToVisualTree(root);
        }

        private static void ApplyToVisualTree(DependencyObject root)
        {
            ApplyThemeResources(root);

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                ApplyToVisualTree(VisualTreeHelper.GetChild(root, index));
        }

        private static void ApplyThemeResources(DependencyObject element)
        {
            if (element is Window window)
            {
                window.SetResourceReference(Control.BackgroundProperty, "AppWindowBackgroundBrush");
                window.SetResourceReference(Control.ForegroundProperty, "AppBodyTextBrush");
            }
            else if (element is UserControl userControl)
            {
                userControl.SetResourceReference(Control.ForegroundProperty, "AppBodyTextBrush");
            }

            if (element is TextBox textBox)
            {
                textBox.SetResourceReference(Control.BackgroundProperty,
                    textBox.IsReadOnly ? "AppReadOnlyBackgroundBrush" : "AppInputBackgroundBrush");
                textBox.SetResourceReference(Control.ForegroundProperty, "AppInputForegroundBrush");
                textBox.SetResourceReference(Control.BorderBrushProperty, "AppBorderBrush");
                return;
            }

            if (element is ComboBox or DatePicker or ListBox)
            {
                ((FrameworkElement)element).SetResourceReference(Control.BackgroundProperty, "AppInputBackgroundBrush");
                ((FrameworkElement)element).SetResourceReference(Control.ForegroundProperty, "AppInputForegroundBrush");
                ((FrameworkElement)element).SetResourceReference(Control.BorderBrushProperty, "AppBorderBrush");
                return;
            }

            if (element is DataGrid dataGrid)
            {
                dataGrid.SetResourceReference(Control.BackgroundProperty, "AppSurfaceBrush");
                dataGrid.SetResourceReference(Control.ForegroundProperty, "AppBodyTextBrush");
                dataGrid.SetResourceReference(Control.BorderBrushProperty, "AppBorderBrush");
                dataGrid.SetResourceReference(DataGrid.RowBackgroundProperty, "AppSurfaceBrush");
                dataGrid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "AppAlternateRowBrush");
                dataGrid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "AppBorderBrush");
                dataGrid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, "AppBorderBrush");
                return;
            }

            if (element is TextBlock textBlock && IsNeutralTextBrush(textBlock.Foreground))
            {
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppBodyTextBrush");
                return;
            }

            if (element is Label label && IsNeutralTextBrush(label.Foreground))
            {
                label.SetResourceReference(Control.ForegroundProperty, "AppBodyTextBrush");
                return;
            }

            if (element is Border border && TryGetThemeBackground(border.Background, out var borderResource))
            {
                border.SetResourceReference(Border.BackgroundProperty, borderResource);
                border.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
                return;
            }

            if (element is Panel panel && TryGetThemeBackground(panel.Background, out var panelResource))
                panel.SetResourceReference(Panel.BackgroundProperty, panelResource);
        }

        private static bool TryGetThemeBackground(Brush? brush, out string resourceName)
        {
            resourceName = string.Empty;
            if (brush is not SolidColorBrush solid)
                return false;

            var color = solid.Color;
            if (color == Colors.Transparent)
                return false;

            if (color.R <= 60 && color.G <= 80 && color.B <= 100)
            {
                resourceName = "AppHeaderBackgroundBrush";
                return true;
            }

            if (color.R >= 225 && color.G >= 225 && color.B >= 225)
            {
                resourceName = "AppSurfaceBrush";
                return true;
            }

            return false;
        }

        private static bool IsNeutralTextBrush(Brush? brush)
        {
            if (brush == null)
                return true;

            if (brush is not SolidColorBrush solid || solid.Color == Colors.White)
                return false;

            var color = solid.Color;
            var spread = Math.Max(color.R, Math.Max(color.G, color.B)) -
                         Math.Min(color.R, Math.Min(color.G, color.B));
            return spread <= 35 || (color.R <= 60 && color.G <= 90 && color.B <= 110);
        }
    }
}
