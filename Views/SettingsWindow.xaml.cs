using System;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Instance;

            // Устанавливаем текущую тему в ComboBox
            if (_settings.Theme == "Dark")
            {
                ThemeComboBox.SelectedIndex = 1;
            }
            else
            {
                ThemeComboBox.SelectedIndex = 0;
            }

            UpdateCurrentThemeText();
        }

        /// <summary>
        /// Обработчик изменения темы
        /// </summary>
        private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is ComboBoxItem item)
            {
                var theme = item.Tag?.ToString() ?? "Default";

                try
                {
                    // Сохраняем тему в настройках
                    _settings.Theme = theme;
                    _settings.Save();

                    // Применяем тему сразу
                    ThemeService.Apply(theme);

                    // Обновляем текст
                    UpdateCurrentThemeText();

                    // Показываем уведомление
                    System.Diagnostics.Debug.WriteLine($"✅ Тема изменена на: {theme}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка применения темы: {ex.Message}",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Обновление текста текущей темы
        /// </summary>
        private void UpdateCurrentThemeText()
        {
            var themeName = _settings.Theme == "Dark" ? "Темная" : "Светлая";
            CurrentThemeText.Text = $"Текущая тема: {themeName}";
        }

        /// <summary>
        /// Закрытие окна
        /// </summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}