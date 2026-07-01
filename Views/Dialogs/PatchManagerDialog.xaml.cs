using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BIS.ERP.Views.Dialogs
{
    public partial class PatchManagerDialog : Window
    {
        private readonly BisPatchService _patchService;
        private readonly ObservableCollection<SystemPatchRecord> _patches = new();

        public PatchManagerDialog(AppDbContext context)
        {
            InitializeComponent();
            _patchService = new BisPatchService(context);
            PatchesGrid.ItemsSource = _patches;
            Loaded += async (_, _) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Загрузка истории...";
                var patches = await _patchService.GetHistoryAsync();
                _patches.Clear();
                foreach (var patch in patches)
                    _patches.Add(patch);
                StatusText.Text = $"Патчей в истории: {_patches.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки истории";
                MessageBox.Show($"Ошибка загрузки истории патчей: {ex.Message}", "Патчи",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnApplyPatchClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Загрузить патч инфобазы",
                Filter = "BIS patch (*.bispatch)|*.bispatch|Все файлы (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Проверка патча...";
                var info = await _patchService.InspectPatchAsync(dialog.FileName);
                Mouse.OverrideCursor = null;

                var dependencies = info.Manifest.Dependencies.Count == 0
                    ? "нет"
                    : string.Join(", ", info.Manifest.Dependencies);
                var confirm = MessageBox.Show(
                    $"Применить патч?\n\n" +
                    $"Патч: {info.Manifest.PatchId}\n" +
                    $"Версия: {info.Manifest.Version}\n" +
                    $"Наименование: {info.Manifest.Name}\n" +
                    $"Зависимости: {dependencies}\n" +
                    $"Файлов внутри: {info.Entries.Count}\n" +
                    $"Контрольная сумма: {info.Checksum[..Math.Min(12, info.Checksum.Length)]}...\n\n" +
                    "Перед применением рекомендуется иметь свежую выгрузку инфобазы.",
                    "Применение патча",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    StatusText.Text = "Применение отменено";
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Применение патча...";
                var result = await _patchService.ApplyPatchAsync(dialog.FileName);
                await ServiceLocator.InfoBaseManager.RefreshCurrentInfoBaseVersionAsync();
                await RefreshAsync();

                MessageBox.Show(
                    $"Патч применен.\n\n" +
                    $"Метаданных: {result.MetadataObjects}\n" +
                    $"Отчетов: {result.Reports}\n" +
                    $"Модулей/разделов: {result.Modules}\n" +
                    $"Строк данных: {result.DataRows}\n" +
                    $"SQL: {(result.SchemaApplied ? "выполнен" : "нет")}",
                    "Патчи",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка применения";
                MessageBox.Show($"Ошибка применения патча: {ex.Message}", "Патчи",
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
