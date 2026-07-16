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
        private readonly bool _allowSystemLogoManagement;
        private byte[]? systemLogoImage;
        private string? systemLogoContentType;
        private string? systemLogoFileName;

        public SettingsWindow(bool allowSystemLogoManagement = false)
        {
            InitializeComponent();
            _settings = AppSettings.Instance;
            _allowSystemLogoManagement = allowSystemLogoManagement;
            LoadSystemLogoButton.IsEnabled = _allowSystemLogoManagement;
            ClearSystemLogoButton.IsEnabled = _allowSystemLogoManagement;
            SystemLogoRestrictionText.Visibility = _allowSystemLogoManagement
                ? Visibility.Collapsed
                : Visibility.Visible;

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
                systemLogoImage = configuration.LogoImage;
                systemLogoContentType = configuration.LogoContentType;
                systemLogoFileName = configuration.LogoFileName;
                UpdateSystemLogoPreview();
                DescriptionBox.Text = configuration.Description;
                CompanyDetailsBox.Text = configuration.CompanyDetails;
                EmailBox.Text = configuration.Email;
                PhoneBox.Text = configuration.Phone;
                AppUpdateUrlBox.Text = configuration.AppUpdateUrl;
            };
        }

        private void OnLoadSystemLogoClick(object sender, RoutedEventArgs e)
        {
            if (!_allowSystemLogoManagement)
                return;

            if (!LogoFileService.TryPickLogo(this, out var logo, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    MessageBox.Show(error, "Логотип системы", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            systemLogoImage = logo!.ImageBytes;
            systemLogoContentType = logo.ContentType;
            systemLogoFileName = logo.FileName;
            UpdateSystemLogoPreview();
        }

        private void OnClearSystemLogoClick(object sender, RoutedEventArgs e)
        {
            if (!_allowSystemLogoManagement)
                return;

            systemLogoImage = null;
            systemLogoContentType = null;
            systemLogoFileName = null;
            UpdateSystemLogoPreview();
        }

        private void UpdateSystemLogoPreview()
        {
            var imageSource = LogoFileService.CreateImageSource(systemLogoImage);
            SystemLogoPreviewImage.Source = imageSource;
            SystemLogoPreviewImage.Visibility = imageSource == null ? Visibility.Collapsed : Visibility.Visible;
            SystemLogoPlaceholderText.Text = string.IsNullOrWhiteSpace(SystemIconBox.Text)
                ? "🏢"
                : SystemIconBox.Text.Trim();
            SystemLogoPlaceholderText.Visibility = imageSource == null ? Visibility.Visible : Visibility.Collapsed;
            SystemLogoNameText.Text = string.IsNullOrWhiteSpace(systemLogoFileName)
                ? "Логотип не загружен"
                : $"Загружен: {systemLogoFileName}";
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

        private void OnSystemIconTextChanged(object sender, TextChangedEventArgs e)
        {
            if (SystemLogoPreviewImage != null && SystemLogoPreviewImage.Visibility != Visibility.Visible)
                UpdateSystemLogoPreview();
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
                AppUpdateUrlBox.Text,
                systemLogoImage,
                systemLogoContentType,
                systemLogoFileName,
                updateLogo: _allowSystemLogoManagement);
            _settings.Save();
            DialogResult = true;
            Close();
        }
    }
}
