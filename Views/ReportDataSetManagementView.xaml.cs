using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP.Views
{
    public partial class ReportDataSetManagementView : UserControl
    {
        private readonly AppDbContext _context;
        private readonly ReportDataSetService _service;
        private readonly ObservableCollection<ReportDataSet> _dataSets = new();
        private ReportDataSet? _selected;
        private bool _isLoading;

        public ReportDataSetManagementView(AppDbContext context)
        {
            InitializeComponent();
            _context = context;
            _service = new ReportDataSetService(_context);
            DataSetsGrid.ItemsSource = _dataSets;
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (_isLoading)
                return;

            _isLoading = true;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var selectedId = _selected?.Id;
                await _service.EnsureStandardDataSetsAsync();
                _dataSets.Clear();
                foreach (var dataSet in await _service.GetDataSetsAsync())
                    _dataSets.Add(dataSet);

                var selectedDataSet = selectedId.HasValue
                    ? _dataSets.FirstOrDefault(item => item.Id == selectedId.Value) ?? _dataSets.FirstOrDefault()
                    : _dataSets.FirstOrDefault();

                DataSetsGrid.SelectedItem = selectedDataSet;
                _selected = selectedDataSet;
                FillEditor(_selected);
                SetStatus($"Наборов данных: {_dataSets.Count}");
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка загрузки SQL-наборов данных отчетов.", "ReportDataSetManagementView", ex);
                MessageBox.Show($"Ошибка загрузки наборов данных: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isLoading = false;
            }
        }

        private void OnDataSetSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading)
                return;

            _selected = DataSetsGrid.SelectedItem as ReportDataSet;
            FillEditor(_selected);
        }

        private void FillEditor(ReportDataSet? dataSet)
        {
            CodeBox.Text = dataSet?.Code ?? string.Empty;
            NameBox.Text = dataSet?.Name ?? string.Empty;
            DescriptionBox.Text = dataSet?.Description ?? string.Empty;
            SqlTextBox.Text = dataSet?.SqlText ?? string.Empty;
            IsActiveCheck.IsChecked = dataSet?.IsActive ?? true;
            IsSystemCheck.IsChecked = dataSet?.IsSystem ?? false;
            FieldsGrid.ItemsSource = dataSet?.Fields.OrderBy(field => field.Order).ToList();
        }

        private void OnNewClick(object sender, RoutedEventArgs e)
        {
            _selected = null;
            DataSetsGrid.SelectedItem = null;
            FillEditor(new ReportDataSet { IsActive = true });
            CodeBox.Focus();
            SetStatus("Новый набор данных.");
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            await SaveCurrentAsync(refreshFields: true);
        }

        private async void OnRefreshFieldsClick(object sender, RoutedEventArgs e)
        {
            await SaveCurrentAsync(refreshFields: true);
        }

        private async void OnTestClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var candidate = BuildFromEditor();
                var table = await _service.TestAsync(candidate, 50);
                SetStatus($"Тест выполнен: колонок {table.Columns.Count}, строк {table.Rows.Count}.");
                MessageBox.Show(
                    $"SQL выполнен успешно.\nКолонок: {table.Columns.Count}\nСтрок теста: {table.Rows.Count}",
                    "Тест набора данных",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка теста SQL-набора данных отчета.", "ReportDataSetManagementView", ex);
                MessageBox.Show($"Ошибка теста SQL:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            var answer = MessageBox.Show(
                $"Удалить набор данных \"{_selected.Name}\"?\nСвязанный источник отчета тоже будет удален.",
                "Удаление набора данных",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;

            try
            {
                await _service.DeleteAsync(_selected.Id);
                await LoadAsync();
                SetStatus("Набор данных удален.");
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка удаления SQL-набора данных отчета.", "ReportDataSetManagementView", ex);
                MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveCurrentAsync(bool refreshFields)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var dataSet = BuildFromEditor();
                var saved = await _service.SaveAsync(dataSet);
                if (refreshFields)
                    await _service.SyncMetadataSourceAsync(saved);

                await LoadAsync();
                DataSetsGrid.SelectedItem = _dataSets.FirstOrDefault(item => item.Id == saved.Id);
                SetStatus("Набор данных сохранен, поля и источник отчета обновлены.");
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка сохранения SQL-набора данных отчета.", "ReportDataSetManagementView", ex);
                MessageBox.Show($"Ошибка сохранения набора данных:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private ReportDataSet BuildFromEditor()
        {
            var code = CodeBox.Text.Trim();
            var name = NameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Укажите название набора данных.");

            return new ReportDataSet
            {
                Id = _selected?.Id ?? Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = DescriptionBox.Text.Trim(),
                SqlText = SqlTextBox.Text,
                IsActive = IsActiveCheck.IsChecked == true,
                IsSystem = IsSystemCheck.IsChecked == true,
                MetadataObjectId = _selected?.MetadataObjectId,
                CreatedAt = _selected?.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private void SetStatus(string text)
        {
            StatusText.Text = text;
        }
    }
}

