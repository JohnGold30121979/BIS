using BIS.ERP.Data;
using BIS.ERP.Services;
using BIS.ERP.Models;
using System;
using System.Windows;
using System.Windows.Input;

namespace BIS.ERP.Views.Dialogs
{
    public partial class AboutSystemDialog : Window
    {
        private SystemConfiguration? _configuration;
        private readonly AppUpdatePackageService _updateService = new();

        public AboutSystemDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _configuration = await new SystemConfigurationService().GetAsync();
            Title = $"О системе {_configuration.SystemName}";
            LogoDisplayHelper.Apply(LogoImage, IconText, _configuration.LogoImage, _configuration.Icon);
            SystemNameText.Text = _configuration.SystemName;
            DescriptionText.Text = EmptyFallback(_configuration.Description);
            CompanyDetailsText.Text = EmptyFallback(_configuration.CompanyDetails);
            EmailText.Text = EmptyFallback(_configuration.Email);
            PhoneText.Text = EmptyFallback(_configuration.Phone);
            // Подробная версия программы выводится здесь, а не в шапке окна:
            // в шапке она сокращённая, чтобы не перекрывать название системы.
            UpdateStatusText.Text = string.Empty;
            VersionProgramText.Text = $"Версия программы: {AppVersion.Display}";

            // Версия конфигурации пишется в базу при выгрузке и загрузке конфигурации.
            try
            {
                var configurationVersion = await new ConfigurationExchangeService(new AppDbContext())
                    .GetConfigurationVersionAsync();
                ConfigurationVersionText.Text = $"Версия конфигурации: {configurationVersion}";
            }
            catch
            {
                ConfigurationVersionText.Text = "Версия конфигурации: не определена";
            }
        }

        private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_configuration == null)
                    _configuration = await new SystemConfigurationService().GetAsync();

                if (string.IsNullOrWhiteSpace(_configuration.AppUpdateUrl))
                {
                    MessageBox.Show(
                        "В настройках системы не указана ссылка проверки обновлений.\n\nОткройте «Настройки системы» и заполните поле «Ссылка проверки обновлений программы».",
                        "Проверка обновлений",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                CheckUpdateButton.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;
                UpdateStatusText.Text = "Проверка обновлений...";

                var check = await _updateService.CheckOnlineUpdateAsync(_configuration.AppUpdateUrl);
                Mouse.OverrideCursor = null;

                if (!check.IsUpdateAvailable)
                {
                    UpdateStatusText.Text = $"Обновлений нет. Текущая версия: {check.CurrentVersion}.";
                    MessageBox.Show(
                        $"Установлена актуальная версия.\n\nТекущая версия: {check.CurrentVersion}\nВерсия на сервере: {check.LatestVersion}",
                        "Проверка обновлений",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var confirm = MessageBox.Show(
                    $"Доступно обновление программы.\n\n" +
                    $"Текущая версия: {check.CurrentVersion}\n" +
                    $"Новая версия: {check.LatestVersion}\n" +
                    $"Наименование: {check.Name}\n\n" +
                    $"{check.Description}\n\n" +
                    "Скачать и установить обновление? Программа закроется и запустится снова.",
                    "Проверка обновлений",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    UpdateStatusText.Text = "Установка обновления отменена.";
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                UpdateStatusText.Text = "Скачивание обновления...";
                var packagePath = await _updateService.DownloadOnlineUpdatePackageAsync(check);

                UpdateStatusText.Text = "Подготовка обновления...";
                var staged = await _updateService.StageUpdateAsync(packagePath);
                _updateService.LaunchUpdater(staged);

                Mouse.OverrideCursor = null;
                MessageBox.Show(
                    "Обновление скачано и подготовлено.\n\nBIS.ERP сейчас закроется, updater заменит файлы и запустит программу снова.",
                    "Проверка обновлений",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                ApplicationExitService.ShutdownNow();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                UpdateStatusText.Text = "Ошибка проверки обновления.";
                MessageBox.Show($"Ошибка проверки обновления: {ex.Message}", "Проверка обновлений",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
                Mouse.OverrideCursor = null;
            }
        }

        private static string EmptyFallback(string value) =>
            string.IsNullOrWhiteSpace(value) ? "Не указано" : value;
    }
}
