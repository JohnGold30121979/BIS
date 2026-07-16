using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly SystemConfigurationService _systemConfigurationService = new();

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Instance;

            ThemeComboBox.SelectedItem = ThemeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == _settings.Theme)
                ?? ThemeComboBox.Items[0];

            LanguageComboBox.SelectedValue = _settings.Language;

            UpdateCurrentThemeText();
            Loaded += async (_, _) =>
            {
                var configuration = await _systemConfigurationService.GetAsync();
                SystemNameBox.Text = configuration.SystemName;
                SystemIconBox.Text = configuration.Icon;
                DescriptionBox.Text = configuration.Description;
                CompanyDetailsBox.Text = configuration.CompanyDetails;
                EmailBox.Text = configuration.Email;
                PhoneBox.Text = configuration.Phone;
                AppUpdateUrlBox.Text = configuration.AppUpdateUrl;
            };
        }

        private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is not ComboBoxItem item)
                return;

            _settings.Language = item.Tag?.ToString() ?? "ru-RU";
            _settings.Save();
            if (LocalizationService.Current != null)
                await LocalizationService.Current.SetCultureAsync(_settings.Language);
        }

        private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is not ComboBoxItem item)
                return;

            var theme = item.Tag?.ToString() ?? "Default";

            try
            {
                _settings.Theme = theme;
                _settings.Save();

                ThemeService.Apply(theme);
                UpdateCurrentThemeText();

                System.Diagnostics.Debug.WriteLine($"Theme changed to: {theme}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка применения темы: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateCurrentThemeText()
        {
            var themeName = ThemeService.GetDisplayName(_settings.Theme);
            CurrentThemeText.Text = $"Текущая тема: {themeName}";
        }

        private async void OnLoadNationalBankRatesClick(object sender, RoutedEventArgs e)
        {
            LoadNationalBankRatesButton.IsEnabled = false;
            NationalBankRatesStatusText.Text = "Загрузка...";
            var previousCursor = Cursor;
            Cursor = Cursors.Wait;

            try
            {
                await using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var importService = new NationalBankCurrencyRateImportService(context);
                var results = await importService.ImportLatestOfficialRatesAsync();

                var imported = results.Sum(result => result.Imported);
                var skipped = results.Sum(result => result.Skipped);
                var dates = string.Join(", ", results.Select(result => result.RateDate.ToString("dd.MM.yyyy")));
                var message = $"Курсы загружены. Даты: {dates}. Загружено: {imported}, пропущено: {skipped}.";

                NationalBankRatesStatusText.Text = message;
                MessageBox.Show(message, "Загрузка курсов НБКР", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                NationalBankRatesStatusText.Text = "Ошибка загрузки";
                MessageBox.Show($"Ошибка загрузки курсов НБКР: {ex.Message}",
                    "Загрузка курсов НБКР", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = previousCursor;
                LoadNationalBankRatesButton.IsEnabled = true;
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            await _systemConfigurationService.SaveAsync(
                SystemNameBox.Text,
                SystemIconBox.Text,
                DescriptionBox.Text,
                CompanyDetailsBox.Text,
                EmailBox.Text,
                PhoneBox.Text,
                AppUpdateUrlBox.Text);
            _settings.Save();
            DialogResult = true;
            Close();
        }
    }
}
