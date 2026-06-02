using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class ReportDesignerWindow : Window
    {
        private ReportService _reportService;
        private MetadataService _metadataService;
        private Report _currentReport;
        private List<MetadataObject> _availableCatalogs;
        private ObservableCollection<ReportField> _reportFields;
        private ObservableCollection<ReportFilter> _reportFilters;

        public ReportDesignerWindow(Report report = null)
        {
            InitializeComponent();

            Loaded += async (s, e) => await InitializeAsync(report);
        }

        private async Task InitializeAsync(Report report)
        {
            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                _reportService = new ReportService(context);
                _metadataService = new MetadataService(context);

                _reportFields = new ObservableCollection<ReportField>();
                _reportFilters = new ObservableCollection<ReportFilter>();

                ReportFieldsGrid.ItemsSource = _reportFields;
                FiltersList.ItemsSource = _reportFilters;

                await LoadDataSources();

                if (report != null)
                {
                    LoadReport(report);
                }

                DataSourceCombo.SelectionChanged += OnDataSourceChanged;
                AddFilterButton.Click += OnAddFilterClick;
                CancelButton.Click += OnCancelClick;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadDataSources()
        {
            _availableCatalogs = await _metadataService.GetCatalogsAsync();

            DataSourceCombo.Items.Clear();
            DataSourceCombo.Items.Add(new ComboBoxItem { Tag = null, Content = "-- Выберите источник данных --" });

            foreach (var cat in _availableCatalogs)
            {
                DataSourceCombo.Items.Add(new ComboBoxItem
                {
                    Tag = cat,
                    Content = $"📚 {cat.Name}"
                });
            }

            DataSourceCombo.SelectedIndex = 0;
        }

        private async void OnDataSourceChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = DataSourceCombo.SelectedItem as ComboBoxItem;
            if (selected?.Tag is MetadataObject selectedCatalog)
            {
                await LoadAvailableFields(selectedCatalog);
            }
            else
            {
                AvailableFields.ItemsSource = null;
            }
        }

        private async Task LoadAvailableFields(MetadataObject catalog)
        {
            var addedFieldNames = _reportFields.Select(f => f.DisplayName).ToHashSet();

            var fields = new List<FieldDef>();

            // Стандартные поля
            var standardFields = new[] { "Id", "CreatedAt", "UpdatedAt" };
            foreach (var stdField in standardFields)
            {
                if (!addedFieldNames.Contains(stdField))
                {
                    fields.Add(new FieldDef { Name = stdField, Type = "Guid" });
                }
            }

            // Пользовательские поля
            foreach (var field in catalog.Fields.OrderBy(f => f.Order))
            {
                if (!addedFieldNames.Contains(field.Name))
                {
                    fields.Add(new FieldDef { Name = field.Name, Type = field.FieldType });
                }
            }

            AvailableFields.ItemsSource = fields;

            if (!fields.Any())
            {
                var emptyList = new List<FieldDef>
                {
                    new FieldDef { Name = "--- Все поля уже добавлены ---", Type = "" }
                };
                AvailableFields.ItemsSource = emptyList;
            }
        }

        private async void OnFieldMouseDown(object sender, MouseButtonEventArgs e)
        {
            var field = AvailableFields.SelectedItem as FieldDef;
            if (field != null && !string.IsNullOrEmpty(field.Name) && !field.Name.StartsWith("---"))
            {
                if (!_reportFields.Any(f => f.FieldName == field.Name))
                {
                    // Получаем латинское имя поля для БД
                    string dbFieldName = field.Name;
                    string displayName = field.Name;

                    if (DataSourceCombo.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject currentCatalog)
                    {
                        // Ищем соответствие в полях справочника
                        var catalogField = currentCatalog.Fields.FirstOrDefault(f => f.Name == field.Name);
                        if (catalogField != null)
                        {
                            dbFieldName = catalogField.DbColumnName; // Латинское имя для БД
                            displayName = catalogField.Name; // Русское имя для отображения
                        }
                        else
                        {
                            // Стандартные поля
                            dbFieldName = field.Name switch
                            {
                                "Id" => "Id",
                                "CreatedAt" => "CreatedAt",
                                "UpdatedAt" => "UpdatedAt",
                                _ => field.Name.ToLower().Replace(" ", "_")
                            };
                        }
                    }

                    _reportFields.Add(new ReportField
                    {
                        Id = Guid.NewGuid(),
                        FieldName = dbFieldName, // Сохраняем латинское имя
                        DisplayName = displayName, // Отображаем русское имя
                        Order = _reportFields.Count + 1,
                        IsVisible = true,
                        Width = 100
                    });

                    // Обновляем список доступных полей
                    if (DataSourceCombo.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is MetadataObject catalog)
                    {
                        await LoadAvailableFields(catalog);
                    }
                }
            }
        }

        private string GetDbFieldName(string russianName)
        {
            if (DataSourceCombo.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject catalog)
            {
                var field = catalog.Fields.FirstOrDefault(f => f.Name == russianName);
                if (field != null)
                {
                    return field.DbColumnName;
                }
            }

            // Стандартные поля
            return russianName switch
            {
                "Id" => "Id",
                "CreatedAt" => "CreatedAt",
                "UpdatedAt" => "UpdatedAt",
                _ => russianName.ToLower().Replace(" ", "_")
            };
        }

        private void LoadReport(Report report)
        {
            _currentReport = report;
            ReportNameBox.Text = report.Name;
            ReportDescBox.Text = report.Description;

            if (report.DataSourceId.HasValue)
            {
                var foundCatalog = _availableCatalogs.FirstOrDefault(c => c.Id == report.DataSourceId);
                if (foundCatalog != null)
                {
                    var item = DataSourceCombo.Items
                        .Cast<ComboBoxItem>()
                        .FirstOrDefault(i => (i.Tag as MetadataObject)?.Id == foundCatalog.Id);

                    if (item != null)
                        DataSourceCombo.SelectedItem = item;
                }
            }

            _reportFields.Clear();
            foreach (var field in report.Fields.OrderBy(f => f.Order))
            {
                _reportFields.Add(new ReportField
                {
                    Id = field.Id,
                    FieldName = field.FieldName,
                    DisplayName = field.DisplayName,
                    Order = field.Order,
                    Width = field.Width,
                    IsVisible = field.IsVisible,
                    AggregateType = field.AggregateType
                });
            }

            _reportFilters.Clear();
            foreach (var filter in report.Filters.OrderBy(f => f.Order))
            {
                _reportFilters.Add(new ReportFilter
                {
                    Id = filter.Id,
                    FieldName = filter.FieldName,
                    Operation = filter.Operation,
                    Value = filter.Value,
                    Order = filter.Order
                });
            }

            if (DataSourceCombo.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject catalog)
            {
                _ = LoadAvailableFields(catalog);
            }
        }

        private void OnRemoveFieldClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var field = button?.DataContext as ReportField;
            if (field != null)
            {
                _reportFields.Remove(field);

                if (DataSourceCombo.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject catalog)
                {
                    _ = LoadAvailableFields(catalog);
                }
            }
        }

        private async void OnPreviewClick(object sender, RoutedEventArgs e)
        {
            await GenerateReport(true);
        }

        private async void OnRunClick(object sender, RoutedEventArgs e)
        {
            await GenerateReport(false);
        }

        private async Task GenerateReport(bool isPreview)
        {
            try
            {
                var selected = DataSourceCombo.SelectedItem as ComboBoxItem;
                if (selected?.Tag is MetadataObject selectedCatalog)
                {
                    var tempReport = new Report
                    {
                        Name = ReportNameBox.Text,
                        Description = ReportDescBox.Text,
                        DataSourceType = "Catalog",
                        DataSourceId = selectedCatalog.Id,
                        Fields = _reportFields.Where(f => f.IsVisible).ToList(),
                        Filters = _reportFilters.ToList()
                    };

                    var data = await _reportService.GetReportDataAsync(tempReport);

                    if (isPreview)
                    {
                        ShowPreview(data, tempReport);
                    }
                    else
                    {
                        ExportReport(data, tempReport);
                    }
                }
                else
                {
                    MessageBox.Show("Выберите источник данных", "Внимание",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка формирования отчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowPreview(DataTable data, Report report)
        {
            var previewWindow = new ReportPreviewWindow(data, report);
            previewWindow.Owner = this;
            previewWindow.ShowDialog();
        }

        private void ExportReport(DataTable data, Report report)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Сохранить отчет",
                Filter = "Excel файлы (*.xlsx)|*.xlsx|HTML файлы (*.html)|*.html",
                DefaultExt = "xlsx",
                FileName = $"{report.Name}_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    byte[] result;
                    if (saveDialog.FilterIndex == 1)
                    {
                        result = _reportService.ExportToExcel(data, report);
                    }
                    else
                    {
                        var html = _reportService.ExportToHtml(data, report);
                        result = System.Text.Encoding.UTF8.GetBytes(html);
                    }

                    System.IO.File.WriteAllBytes(saveDialog.FileName, result);

                    MessageBox.Show($"Отчет сохранен: {saveDialog.FileName}", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ReportNameBox.Text))
                {
                    MessageBox.Show("Введите название отчета", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selected = DataSourceCombo.SelectedItem as ComboBoxItem;
                if (selected?.Tag is not MetadataObject selectedCatalog)
                {
                    MessageBox.Show("Выберите источник данных (справочник)", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_reportFields == null || !_reportFields.Any())
                {
                    var result = MessageBox.Show("В отчете нет полей. Добавить поля по умолчанию?",
                        "Вопрос", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        foreach (var field in selectedCatalog.Fields)
                        {
                            _reportFields.Add(new ReportField
                            {
                                Id = Guid.NewGuid(),
                                FieldName = field.DbColumnName,
                                DisplayName = field.Name,
                                Order = _reportFields.Count + 1,
                                IsVisible = true,
                                Width = 100
                            });
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                if (_currentReport == null)
                {
                    _currentReport = new Report();
                }

                _currentReport.Name = ReportNameBox.Text;
                _currentReport.Description = ReportDescBox.Text;
                _currentReport.DataSourceType = "Catalog";
                _currentReport.DataSourceId = selectedCatalog.Id;
                _currentReport.Icon = "📊";
                _currentReport.Order = 0;
                _currentReport.UpdatedAt = DateTime.UtcNow;

                if (_currentReport.CreatedAt == DateTime.MinValue)
                    _currentReport.CreatedAt = DateTime.UtcNow;

                // Сохраняем поля
                _currentReport.Fields.Clear();
                foreach (var field in _reportFields)
                {
                    _currentReport.Fields.Add(new ReportField
                    {
                        Id = field.Id == Guid.Empty ? Guid.NewGuid() : field.Id,
                        FieldName = field.FieldName,
                        DisplayName = field.DisplayName,
                        Order = field.Order,
                        Width = field.Width,
                        IsVisible = field.IsVisible,
                        AggregateType = field.AggregateType
                    });
                }

                // Сохраняем фильтры
                _currentReport.Filters.Clear();
                foreach (var filter in _reportFilters)
                {
                    if (!string.IsNullOrWhiteSpace(filter.FieldName))
                    {
                        _currentReport.Filters.Add(new ReportFilter
                        {
                            Id = filter.Id == Guid.Empty ? Guid.NewGuid() : filter.Id,
                            FieldName = filter.FieldName,
                            Operation = filter.Operation,
                            Value = filter.Value,
                            Order = filter.Order
                        });
                    }
                }

                await _reportService.SaveReportAsync(_currentReport);

                MessageBox.Show($"Отчет \"{_currentReport.Name}\" успешно сохранен!",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения отчета: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnAddFilterClick(object sender, RoutedEventArgs e)
        {
            _reportFilters.Add(new ReportFilter
            {
                Id = Guid.NewGuid(),
                FieldName = "",
                Operation = "=",
                Value = "",
                Order = _reportFilters.Count + 1
            });
        }

        // Двойной клик по полю для добавления
        private void OnFieldDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var field = AvailableFields.SelectedItem as FieldDef;
            if (field != null)
            {
                OnFieldMouseDown(sender, e);
            }
        }

        // Удаление фильтра
        private void RemoveFilter_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var filter = button?.DataContext as ReportFilter;
            if (filter != null && _reportFilters.Contains(filter))
            {
                _reportFilters.Remove(filter);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class FieldDef
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}