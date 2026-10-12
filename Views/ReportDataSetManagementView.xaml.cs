using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
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

            // Список системных расчётов заполняется один раз: он постоянный.
            CalculatorCombo.ItemsSource = SystemReportCalculators.All;

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

            var isSystem = dataSet != null && ReportDataSetService.IsSystemDataSet(dataSet);

            // Вид набора данных: запрос или готовый расчёт.
            SelectKind(isSystem ? ReportDataSetKinds.System : ReportDataSetKinds.Sql);
            CalculatorCombo.SelectedItem = isSystem
                ? SystemReportCalculators.Find(dataSet!.CalculatorCode)
                : null;

            ApplyKindVisibility();
        }

        /// <summary>Выбирает пункт вида набора данных.</summary>
        private void SelectKind(string kind)
        {
            foreach (var item in KindCombo.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), kind, StringComparison.OrdinalIgnoreCase))
                {
                    KindCombo.SelectedItem = item;
                    return;
                }
            }

            KindCombo.SelectedIndex = 0;
        }

        /// <summary>Выбранный вид набора данных.</summary>
        private string GetSelectedKind() =>
            (KindCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ReportDataSetKinds.Sql;

        private void OnKindChanged(object sender, SelectionChangedEventArgs e) => ApplyKindVisibility();

        private void OnCalculatorChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CalculatorHint == null)
                return;

            var calculator = CalculatorCombo.SelectedItem as SystemReportCalculator;
            CalculatorHint.Text = calculator == null
                ? string.Empty
                : BuildCalculatorHint(calculator);
        }

        /// <summary>
        /// Показывает, что вернёт расчёт и какие параметры у него есть.
        /// Администратору это заменяет чтение документации.
        /// </summary>
        private static string BuildCalculatorHint(SystemReportCalculator calculator)
        {
            var fields = string.Join(", ", calculator.Fields
                .Where(field => !field.DbColumnName.Contains('('))
                .Take(10)
                .Select(field => field.Name));

            var parameters = calculator.Parameters.Count == 0
                ? "без параметров"
                : string.Join(", ", calculator.Parameters.Select(parameter =>
                    string.IsNullOrWhiteSpace(parameter.DefaultValue)
                        ? parameter.Title
                        : $"{parameter.Title} = {parameter.DefaultValue}"));

            return $"{calculator.Description}\n" +
                   $"Параметры: {parameters}.\n" +
                   $"Колонок: {calculator.Fields.Count}. Основные: {fields}.";
        }

        /// <summary>
        /// Показывает только то, что относится к выбранному виду:
        /// либо поле запроса, либо список расчётов.
        /// </summary>
        private void ApplyKindVisibility()
        {
            if (KindCombo == null || SqlTextBox == null)
                return;

            var isSystem = string.Equals(GetSelectedKind(), ReportDataSetKinds.System, StringComparison.OrdinalIgnoreCase);

            if (CalculatorLabel != null)
                CalculatorLabel.Visibility = isSystem ? Visibility.Visible : Visibility.Collapsed;
            if (CalculatorCombo != null)
                CalculatorCombo.Visibility = isSystem ? Visibility.Visible : Visibility.Collapsed;
            if (CalculatorHint != null)
                CalculatorHint.Visibility = isSystem ? Visibility.Visible : Visibility.Collapsed;

            // Поле запроса при системном расчёте не нужно: SQL у него нет.
            SqlTextBox.IsEnabled = !isSystem;
            SqlTextBox.Opacity = isSystem ? 0.5 : 1.0;
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
            const int testLimit = 50;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var candidate = BuildFromEditor();
                var table = await _service.TestAsync(candidate, testLimit);
                SetStatus($"Тест выполнен: колонок {table.Columns.Count}, строк {table.Rows.Count}.");
                Mouse.OverrideCursor = null;

                var dialog = new ReportDataSetTestResultDialog(candidate.Name, table, testLimit)
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
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
                // Остаёмся на только что сохранённом наборе данных.
                DataSetsGrid.SelectRow(_dataSets.FirstOrDefault(item => item.Id == saved.Id));
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

            var kind = GetSelectedKind();
            var isSystem = string.Equals(kind, ReportDataSetKinds.System, StringComparison.OrdinalIgnoreCase);
            var calculator = CalculatorCombo.SelectedItem as SystemReportCalculator;

            if (isSystem && calculator == null)
                throw new InvalidOperationException("Выберите системный расчёт из списка.");

            if (!isSystem && string.IsNullOrWhiteSpace(SqlTextBox.Text))
                throw new InvalidOperationException("Введите SQL-запрос набора данных.");

            return new ReportDataSet
            {
                Id = _selected?.Id ?? Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = DescriptionBox.Text.Trim(),
                Kind = isSystem ? ReportDataSetKinds.System : ReportDataSetKinds.Sql,
                CalculatorCode = isSystem ? calculator!.Code : string.Empty,
                ParameterDefinitions = isSystem ? SerializeParameters(calculator!) : string.Empty,
                SqlText = isSystem ? string.Empty : SqlTextBox.Text,
                IsActive = IsActiveCheck.IsChecked == true,
                IsSystem = IsSystemCheck.IsChecked == true,
                MetadataObjectId = _selected?.MetadataObjectId,
                CreatedAt = _selected?.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Сохраняет параметры расчёта в набор данных: интерфейс построит по ним
        /// поля ввода, поэтому описание должно лежать в базе.
        /// </summary>
        private static string SerializeParameters(SystemReportCalculator calculator) =>
            System.Text.Json.JsonSerializer.Serialize(
                calculator.Parameters.Select(parameter => new
                {
                    parameter.Name,
                    parameter.Title,
                    parameter.ParameterType,
                    parameter.DefaultValue,
                    parameter.IsRequired
                }));

        private void SetStatus(string text)
        {
            StatusText.Text = text;
        }
    }
}



