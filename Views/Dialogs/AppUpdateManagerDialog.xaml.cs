using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BIS.ERP.Views.Dialogs
{
    public partial class AppUpdateManagerDialog : Window
    {
        private readonly AppUpdatePackageService _updateService = new();
        private readonly ObservableCollection<AppUpdateRecord> _updates = new();

        public AppUpdateManagerDialog()
        {
            InitializeComponent();
            UpdatesGrid.ItemsSource = _updates;
            DescriptionText.Text =
                $"Сборка и установка зашифрованных пакетов .bisapp для файлов приложения. Текущая версия: {_updateService.CurrentAppVersion}.";
            Loaded += async (_, _) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Загрузка истории...";
                var updates = await _updateService.GetHistoryAsync();
                _updates.Clear();
                foreach (var update in updates)
                    _updates.Add(update);

                StatusText.Text = $"Записей в истории: {_updates.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки истории";
                MessageBox.Show($"Ошибка загрузки истории обновлений: {ex.Message}", "Обновления программы",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnBuildUpdateClick(object sender, RoutedEventArgs e)
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "Выберите папку публикации программы"
            };

            if (folderDialog.ShowDialog(this) != true)
                return;

            AppUpdateManifest manifest;
            var manifestExists = File.Exists(Path.Combine(folderDialog.FolderName, "manifest.json"));
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Подготовка параметров обновления...";
                manifest = await _updateService.CreateManifestForFolderAsync(folderDialog.FolderName);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка подготовки обновления";
                MessageBox.Show($"Ошибка подготовки параметров обновления: {ex.Message}", "Обновления программы",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            var settingsDialog = new AppUpdateBuildSettingsDialog(manifest, !manifestExists)
            {
                Owner = this
            };
            if (settingsDialog.ShowDialog() != true)
            {
                StatusText.Text = "Сборка обновления отменена";
                return;
            }

            manifest = settingsDialog.Manifest;
            var saveDialog = new SaveFileDialog
            {
                Title = "Сохранить обновление программы",
                Filter = "BIS app update (*.bisapp)|*.bisapp",
                FileName = $"{manifest.UpdateId}.bisapp",
                AddExtension = true,
                DefaultExt = ".bisapp"
            };

            if (saveDialog.ShowDialog(this) != true)
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Сборка .bisapp...";
                await _updateService.CreateUpdateFromFolderAsync(folderDialog.FolderName, saveDialog.FileName, manifest);
                var info = await _updateService.InspectUpdateAsync(saveDialog.FileName);

                StatusText.Text = $"Обновление собрано: {info.Manifest.UpdateId}";
                MessageBox.Show(
                    $"Обновление программы собрано.\n\n" +
                    $"Файл: {saveDialog.FileName}\n" +
                    $"UpdateId: {info.Manifest.UpdateId}\n" +
                    $"Версия: {info.Manifest.Version}\n" +
                    $"Файлов: {info.Manifest.Files.Count}\n" +
                    $"Запуск: {info.Manifest.RestartExecutable}\n" +
                    $"Контрольная сумма: {info.Checksum[..Math.Min(12, info.Checksum.Length)]}...",
                    "Обновления программы",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка сборки обновления";
                MessageBox.Show($"Ошибка сборки обновления: {ex.Message}", "Обновления программы",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnApplyUpdateClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Загрузить обновление программы",
                Filter = "BIS app update (*.bisapp)|*.bisapp|Все файлы (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Проверка обновления...";
                var info = await _updateService.InspectUpdateAsync(dialog.FileName);
                Mouse.OverrideCursor = null;

                var firstFiles = string.Join(Environment.NewLine, info.Manifest.Files
                    .Take(6)
                    .Select(file => $"• {file.RelativePath}"));
                if (info.Manifest.Files.Count > 6)
                    firstFiles += $"{Environment.NewLine}• ...";

                var confirm = MessageBox.Show(
                    $"Установить обновление программы?\n\n" +
                    $"UpdateId: {info.Manifest.UpdateId}\n" +
                    $"Версия: {info.Manifest.Version}\n" +
                    $"Наименование: {info.Manifest.Name}\n" +
                    $"Файлов: {info.Manifest.Files.Count}\n" +
                    $"Запуск после обновления: {info.Manifest.RestartExecutable}\n\n" +
                    $"{firstFiles}\n\n" +
                    "Программа закроется, updater заменит файлы и запустит BIS.ERP снова.",
                    "Установка обновления программы",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    StatusText.Text = "Установка отменена";
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Подготовка обновления...";
                var staged = await _updateService.StageUpdateAsync(dialog.FileName);
                _updateService.LaunchUpdater(staged);

                MessageBox.Show(
                    "Обновление подготовлено.\n\nBIS.ERP сейчас закроется, после замены файлов программа запустится автоматически.",
                    "Обновления программы",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ApplicationExitService.ShutdownNow();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка установки обновления";
                MessageBox.Show($"Ошибка установки обновления: {ex.Message}", "Обновления программы",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                await RefreshAsync();
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
